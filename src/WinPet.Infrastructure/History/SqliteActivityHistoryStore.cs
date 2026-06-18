using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using WinPet.Core.Activity;
using WinPet.Core.History;
using WinPet.Core.Sessions;

namespace WinPet.Infrastructure.History;

public sealed class SqliteActivityHistoryStore :
    IActivityHistoryStore,
    IActivityDataManager
{
    private const int SchemaVersion = 2;
    private readonly string _connectionString;
    private WorkSessionSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRecordedAt;
    private WorkSessionState? _lastSessionState;

    public SqliteActivityHistoryStore(
        string databasePath,
        WorkSessionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        settings.Validate();

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException(
                "The database path must have a parent directory.",
                nameof(databasePath)));

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        _settings = settings;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS schema_info (
                    version INTEGER NOT NULL
                );

                INSERT INTO schema_info (version)
                SELECT $schemaVersion
                WHERE NOT EXISTS (SELECT 1 FROM schema_info);

                CREATE TABLE IF NOT EXISTS activity_buckets (
                    bucket_started_utc TEXT NOT NULL PRIMARY KEY,
                    local_date TEXT NOT NULL,
                    timezone_offset_minutes INTEGER NOT NULL,
                    active_milliseconds INTEGER NOT NULL DEFAULT 0,
                    idle_milliseconds INTEGER NOT NULL DEFAULT 0,
                    locked_milliseconds INTEGER NOT NULL DEFAULT 0,
                    suspended_milliseconds INTEGER NOT NULL DEFAULT 0,
                    overtime_milliseconds INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS
                    ix_activity_buckets_local_date
                    ON activity_buckets(local_date);

                CREATE TABLE IF NOT EXISTS work_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    local_date TEXT NOT NULL,
                    active_milliseconds INTEGER NOT NULL DEFAULT 0,
                    idle_milliseconds INTEGER NOT NULL DEFAULT 0,
                    overtime_milliseconds INTEGER NOT NULL DEFAULT 0,
                    end_reason TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS
                    ix_work_sessions_local_date
                    ON work_sessions(local_date);

                CREATE TABLE IF NOT EXISTS rest_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL,
                    local_date TEXT NOT NULL,
                    duration_milliseconds INTEGER NOT NULL DEFAULT 0,
                    qualified INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS
                    ix_rest_sessions_local_date
                    ON rest_sessions(local_date);

                CREATE TABLE IF NOT EXISTS reminder_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_at_utc TEXT NOT NULL,
                    local_date TEXT NOT NULL,
                    reminder_type TEXT NOT NULL,
                    user_action TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS
                    ix_reminder_events_local_date
                    ON reminder_events(local_date);

                UPDATE schema_info SET version = $schemaVersion;
                """;
            command.Parameters.AddWithValue("$schemaVersion", SchemaVersion);
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateSettings(WorkSessionSettings settings)
    {
        settings.Validate();
        _settings = settings;
    }

    public async Task RecordAsync(
        ActivitySnapshot snapshot,
        WorkSessionUpdate session,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lastRecordedAt is null)
            {
                await EnsureSessionForFirstSnapshotAsync(
                    snapshot,
                    cancellationToken).ConfigureAwait(false);
                _lastRecordedAt = snapshot.Timestamp;
                _lastSessionState = session.State;
                return;
            }

            var startedAt = _lastRecordedAt.Value;
            var endedAt = snapshot.Timestamp;
            if (endedAt <= startedAt)
            {
                _lastRecordedAt = Max(startedAt, endedAt);
                return;
            }

            // A very large gap indicates process suspension, debugging, or a
            // clock adjustment. Keep the raw gap bounded to avoid inventing
            // hours of activity from one stale sample.
            if (endedAt - startedAt > TimeSpan.FromMinutes(10))
            {
                startedAt = endedAt - TimeSpan.FromMinutes(10);
            }

            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await UpdateSessionBoundariesAsync(
                connection,
                (SqliteTransaction)transaction,
                snapshot,
                session,
                cancellationToken).ConfigureAwait(false);
            await RecordReminderTransitionAsync(
                connection,
                (SqliteTransaction)transaction,
                session,
                cancellationToken).ConfigureAwait(false);

            while (startedAt < endedAt)
            {
                var bucketStart = FloorToMinute(startedAt);
                var bucketEnd = bucketStart.AddMinutes(1);
                var segmentEnd = Min(bucketEnd, endedAt);
                var duration = segmentEnd - startedAt;

                await UpsertSegmentAsync(
                    connection,
                    transaction,
                    bucketStart,
                    duration,
                    snapshot,
                    session,
                    cancellationToken).ConfigureAwait(false);
                await AddSegmentToOpenSessionAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    duration,
                    snapshot,
                    session,
                    cancellationToken).ConfigureAwait(false);
                startedAt = segmentEnd;
            }

            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            _lastRecordedAt = endedAt;
            _lastSessionState = session.State;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RecordManualResetAsync(
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default) =>
        CloseWorkAndStartNewAsync(
            timestamp,
            WorkSessionEndReason.ManualReset,
            startNewSession: true,
            cancellationToken);

    public Task RecordApplicationStoppedAsync(
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default) =>
        CloseWorkAndStartNewAsync(
            timestamp,
            WorkSessionEndReason.ApplicationStopped,
            startNewSession: false,
            cancellationToken);

    public async Task<DailyActivitySummary> GetDailySummaryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    COALESCE(SUM(active_milliseconds), 0),
                    COALESCE(SUM(idle_milliseconds), 0),
                    COALESCE(SUM(locked_milliseconds), 0),
                    COALESCE(SUM(suspended_milliseconds), 0),
                    COALESCE(SUM(overtime_milliseconds), 0),
                    (
                        SELECT COUNT(*)
                        FROM work_sessions
                        WHERE local_date = $localDate
                          AND ended_at_utc IS NOT NULL
                    ),
                    (
                        SELECT COUNT(*)
                        FROM rest_sessions
                        WHERE local_date = $localDate
                          AND qualified = 1
                    ),
                    (
                        SELECT COALESCE(
                            MAX(
                                CASE
                                    WHEN ended_at_utc IS NOT NULL THEN
                                        CAST(ROUND(
                                            (
                                                julianday(ended_at_utc) -
                                                julianday(started_at_utc)
                                            ) * 86400000
                                        ) AS INTEGER)
                                    ELSE
                                        active_milliseconds +
                                        idle_milliseconds
                                END
                            ),
                            0)
                        FROM work_sessions
                        WHERE local_date = $localDate
                    ),
                    (
                        SELECT COUNT(*)
                        FROM reminder_events
                        WHERE local_date = $localDate
                    )
                FROM activity_buckets
                WHERE local_date = $localDate;
                """;
            command.Parameters.AddWithValue(
                "$localDate",
                localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

            return new DailyActivitySummary(
                localDate,
                TimeSpan.FromMilliseconds(reader.GetInt64(0)),
                TimeSpan.FromMilliseconds(reader.GetInt64(1)),
                TimeSpan.FromMilliseconds(reader.GetInt64(2)),
                TimeSpan.FromMilliseconds(reader.GetInt64(3)),
                TimeSpan.FromMilliseconds(reader.GetInt64(4)),
                reader.GetInt32(5),
                reader.GetInt32(6),
                TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                reader.GetInt32(8));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HourlyActivitySummary>>
        GetHourlyActivityAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = new long[24];
            var idle = new long[24];
            var overtime = new long[24];

            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    bucket_started_utc,
                    timezone_offset_minutes,
                    active_milliseconds,
                    idle_milliseconds,
                    overtime_milliseconds
                FROM activity_buckets
                WHERE local_date = $localDate
                ORDER BY bucket_started_utc;
                """;
            command.Parameters.AddWithValue(
                "$localDate",
                localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                var utc = DateTimeOffset.Parse(
                    reader.GetString(0),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal);
                var offset = TimeSpan.FromMinutes(reader.GetInt32(1));
                var hour = utc.ToOffset(offset).Hour;
                active[hour] += reader.GetInt64(2);
                idle[hour] += reader.GetInt64(3);
                overtime[hour] += reader.GetInt64(4);
            }

            return Enumerable.Range(0, 24)
                .Select(hour => new HourlyActivitySummary(
                    hour,
                    TimeSpan.FromMilliseconds(active[hour]),
                    TimeSpan.FromMilliseconds(idle[hour]),
                    TimeSpan.FromMilliseconds(overtime[hour])))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DailyTrendPoint>> GetDailyTrendAsync(
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(nameof(endDate));
        }

        var points = new List<DailyTrendPoint>();
        for (var date = startDate;
             date <= endDate;
             date = date.AddDays(1))
        {
            var summary = await GetDailySummaryAsync(
                date,
                cancellationToken).ConfigureAwait(false);
            points.Add(new DailyTrendPoint(
                date,
                summary.ActiveDuration,
                summary.ComputerDuration,
                summary.OvertimeDuration,
                summary.QualifiedBreaks));
        }

        return points;
    }

    public async Task<string> ExportCsvArchiveAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var exportName = $"WinPet-{DateTime.Now:yyyyMMdd-HHmmss}";
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            exportName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            foreach (var table in new[]
                     {
                         "activity_buckets",
                         "work_sessions",
                         "rest_sessions",
                         "reminder_events",
                     })
            {
                await ExportTableAsync(
                    connection,
                    table,
                    Path.Combine(temporaryDirectory, table + ".csv"),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        var archivePath = Path.Combine(outputDirectory, exportName + ".zip");
        try
        {
            ZipFile.CreateFromDirectory(temporaryDirectory, archivePath);
            return archivePath;
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    public async Task ClearAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM activity_buckets;
                DELETE FROM work_sessions;
                DELETE FROM rest_sessions;
                DELETE FROM reminder_events;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            _lastRecordedAt = null;
            _lastSessionState = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task UpsertSegmentAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        DateTimeOffset bucketStart,
        TimeSpan duration,
        ActivitySnapshot snapshot,
        WorkSessionUpdate session,
        CancellationToken cancellationToken)
    {
        var durationMilliseconds = (long)Math.Round(
            duration.TotalMilliseconds,
            MidpointRounding.AwayFromZero);
        var active = 0L;
        var idle = 0L;
        var locked = 0L;
        var suspended = 0L;

        if (snapshot.IsSystemSuspended)
        {
            suspended = durationMilliseconds;
        }
        else if (snapshot.IsSessionLocked)
        {
            locked = durationMilliseconds;
        }
        else if (snapshot.IdleDuration <= _settings.ActiveInputWindow)
        {
            active = durationMilliseconds;
        }
        else
        {
            idle = durationMilliseconds;
        }

        var overtime =
            session.State == WorkSessionState.BreakDue && active > 0
                ? durationMilliseconds
                : 0L;
        var localBucket = bucketStart.ToLocalTime();

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO activity_buckets (
                bucket_started_utc,
                local_date,
                timezone_offset_minutes,
                active_milliseconds,
                idle_milliseconds,
                locked_milliseconds,
                suspended_milliseconds,
                overtime_milliseconds
            )
            VALUES (
                $bucketStartedUtc,
                $localDate,
                $timezoneOffsetMinutes,
                $active,
                $idle,
                $locked,
                $suspended,
                $overtime
            )
            ON CONFLICT(bucket_started_utc) DO UPDATE SET
                active_milliseconds =
                    active_milliseconds + excluded.active_milliseconds,
                idle_milliseconds =
                    idle_milliseconds + excluded.idle_milliseconds,
                locked_milliseconds =
                    locked_milliseconds + excluded.locked_milliseconds,
                suspended_milliseconds =
                    suspended_milliseconds + excluded.suspended_milliseconds,
                overtime_milliseconds =
                    overtime_milliseconds + excluded.overtime_milliseconds;
            """;
        command.Parameters.AddWithValue(
            "$bucketStartedUtc",
            bucketStart.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$localDate",
            DateOnly.FromDateTime(localBucket.DateTime)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$timezoneOffsetMinutes",
            (int)localBucket.Offset.TotalMinutes);
        command.Parameters.AddWithValue("$active", active);
        command.Parameters.AddWithValue("$idle", idle);
        command.Parameters.AddWithValue("$locked", locked);
        command.Parameters.AddWithValue("$suspended", suspended);
        command.Parameters.AddWithValue("$overtime", overtime);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureSessionForFirstSnapshotAsync(
        ActivitySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(
            cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (IsActive(snapshot))
        {
            await EnsureOpenWorkSessionAsync(
                connection,
                (SqliteTransaction)transaction,
                snapshot.Timestamp,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateSessionBoundariesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActivitySnapshot snapshot,
        WorkSessionUpdate session,
        CancellationToken cancellationToken)
    {
        if (session.QualifiedBreakCompleted)
        {
            var breakStartedAt =
                snapshot.Timestamp - session.CurrentBreakDuration;
            var currentSegmentDuration = _lastRecordedAt is { } last
                ? snapshot.Timestamp - last
                : TimeSpan.Zero;
            var priorBreakDuration =
                session.CurrentBreakDuration - currentSegmentDuration;
            if (priorBreakDuration < TimeSpan.Zero)
            {
                priorBreakDuration = TimeSpan.Zero;
            }

            await ReclassifyOpenWorkIdleAsync(
                connection,
                transaction,
                priorBreakDuration,
                cancellationToken).ConfigureAwait(false);
            await CloseOpenWorkSessionAsync(
                connection,
                transaction,
                breakStartedAt,
                WorkSessionEndReason.QualifiedBreak,
                cancellationToken).ConfigureAwait(false);
            await EnsureOpenRestSessionAsync(
                connection,
                transaction,
                breakStartedAt,
                priorBreakDuration,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (IsActive(snapshot))
        {
            await CloseOpenRestSessionAsync(
                connection,
                transaction,
                snapshot.Timestamp,
                qualified: true,
                cancellationToken).ConfigureAwait(false);
            await EnsureOpenWorkSessionAsync(
                connection,
                transaction,
                snapshot.Timestamp,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddSegmentToOpenSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TimeSpan duration,
        ActivitySnapshot snapshot,
        WorkSessionUpdate session,
        CancellationToken cancellationToken)
    {
        var milliseconds = ToMilliseconds(duration);
        if (await HasOpenRestSessionAsync(
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false))
        {
            await using var restCommand = connection.CreateCommand();
            restCommand.Transaction = transaction;
            restCommand.CommandText =
                """
                UPDATE rest_sessions
                SET duration_milliseconds =
                    duration_milliseconds + $duration
                WHERE id = (
                    SELECT id FROM rest_sessions
                    WHERE ended_at_utc IS NULL
                    ORDER BY id DESC
                    LIMIT 1
                );
                """;
            restCommand.Parameters.AddWithValue("$duration", milliseconds);
            await restCommand.ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var active = IsActive(snapshot) ? milliseconds : 0L;
        var idle = active == 0 ? milliseconds : 0L;
        var overtime =
            active > 0 && session.State == WorkSessionState.BreakDue
                ? milliseconds
                : 0L;

        await using var workCommand = connection.CreateCommand();
        workCommand.Transaction = transaction;
        workCommand.CommandText =
            """
            UPDATE work_sessions
            SET
                active_milliseconds = active_milliseconds + $active,
                idle_milliseconds = idle_milliseconds + $idle,
                overtime_milliseconds =
                    overtime_milliseconds + $overtime
            WHERE id = (
                SELECT id FROM work_sessions
                WHERE ended_at_utc IS NULL
                ORDER BY id DESC
                LIMIT 1
            );
            """;
        workCommand.Parameters.AddWithValue("$active", active);
        workCommand.Parameters.AddWithValue("$idle", idle);
        workCommand.Parameters.AddWithValue("$overtime", overtime);
        await workCommand.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CloseWorkAndStartNewAsync(
        DateTimeOffset timestamp,
        WorkSessionEndReason reason,
        bool startNewSession,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(
                cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await CloseOpenWorkSessionAsync(
                connection,
                (SqliteTransaction)transaction,
                timestamp,
                reason,
                cancellationToken).ConfigureAwait(false);
            await CloseOpenRestSessionAsync(
                connection,
                (SqliteTransaction)transaction,
                timestamp,
                qualified: false,
                cancellationToken).ConfigureAwait(false);

            if (reason == WorkSessionEndReason.ManualReset)
            {
                await MarkLatestReminderActionAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    reason.ToString(),
                    cancellationToken).ConfigureAwait(false);
            }

            if (startNewSession)
            {
                await EnsureOpenWorkSessionAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    timestamp,
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            _lastRecordedAt = timestamp;
            _lastSessionState = WorkSessionState.Working;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task EnsureOpenWorkSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO work_sessions (started_at_utc, local_date)
            SELECT $startedAtUtc, $localDate
            WHERE NOT EXISTS (
                SELECT 1 FROM work_sessions
                WHERE ended_at_utc IS NULL
            );
            """;
        command.Parameters.AddWithValue(
            "$startedAtUtc",
            ToUtcText(timestamp));
        command.Parameters.AddWithValue(
            "$localDate",
            ToLocalDateText(timestamp));
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CloseOpenWorkSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset timestamp,
        WorkSessionEndReason reason,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE work_sessions
            SET ended_at_utc = $endedAtUtc,
                end_reason = $endReason
            WHERE ended_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$endedAtUtc", ToUtcText(timestamp));
        command.Parameters.AddWithValue("$endReason", reason.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureOpenRestSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset timestamp,
        TimeSpan initialDuration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO rest_sessions (
                started_at_utc,
                local_date,
                duration_milliseconds,
                qualified
            )
            SELECT $startedAtUtc, $localDate, $duration, 1
            WHERE NOT EXISTS (
                SELECT 1 FROM rest_sessions
                WHERE ended_at_utc IS NULL
            );
            """;
        command.Parameters.AddWithValue("$startedAtUtc", ToUtcText(timestamp));
        command.Parameters.AddWithValue("$localDate", ToLocalDateText(timestamp));
        command.Parameters.AddWithValue(
            "$duration",
            ToMilliseconds(initialDuration));
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ReclassifyOpenWorkIdleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE work_sessions
            SET idle_milliseconds = MAX(
                0,
                idle_milliseconds - $duration
            )
            WHERE ended_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "$duration",
            ToMilliseconds(duration));
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CloseOpenRestSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset timestamp,
        bool qualified,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE rest_sessions
            SET ended_at_utc = $endedAtUtc,
                qualified = CASE
                    WHEN qualified = 1 OR $qualified = 1 THEN 1
                    ELSE 0
                END
            WHERE ended_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$endedAtUtc", ToUtcText(timestamp));
        command.Parameters.AddWithValue("$qualified", qualified ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> HasOpenRestSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM rest_sessions
                WHERE ended_at_utc IS NULL
            );
            """;
        var result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private async Task RecordReminderTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WorkSessionUpdate session,
        CancellationToken cancellationToken)
    {
        var isReminderState =
            session.State is WorkSessionState.Warning
                or WorkSessionState.BreakDue;
        if (!isReminderState || _lastSessionState == session.State)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO reminder_events (
                occurred_at_utc,
                local_date,
                reminder_type
            )
            VALUES ($occurredAtUtc, $localDate, $reminderType);
            """;
        command.Parameters.AddWithValue(
            "$occurredAtUtc",
            ToUtcText(session.Timestamp));
        command.Parameters.AddWithValue(
            "$localDate",
            ToLocalDateText(session.Timestamp));
        command.Parameters.AddWithValue(
            "$reminderType",
            session.State.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task MarkLatestReminderActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string action,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE reminder_events
            SET user_action = $action
            WHERE id = (
                SELECT id
                FROM reminder_events
                WHERE user_action IS NULL
                ORDER BY id DESC
                LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$action", action);
        await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsActive(ActivitySnapshot snapshot) =>
        !snapshot.IsSessionLocked &&
        !snapshot.IsSystemSuspended &&
        snapshot.IdleDuration <= _settings.ActiveInputWindow;

    private static long ToMilliseconds(TimeSpan duration) =>
        (long)Math.Round(
            duration.TotalMilliseconds,
            MidpointRounding.AwayFromZero);

    private static string ToUtcText(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string ToLocalDateText(DateTimeOffset timestamp) =>
        DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task ExportTableAsync(
        SqliteConnection connection,
        string table,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table};";
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var writer = new StreamWriter(
            outputPath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var headers = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName);
        await writer.WriteLineAsync(
            string.Join(",", headers.Select(EscapeCsv)));

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index)
                    ? string.Empty
                    : Convert.ToString(
                        reader.GetValue(index),
                        CultureInfo.InvariantCulture) ?? string.Empty);
            await writer.WriteLineAsync(
                string.Join(",", values.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static DateTimeOffset FloorToMinute(DateTimeOffset timestamp) =>
        new(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            timestamp.Hour,
            timestamp.Minute,
            0,
            timestamp.Offset);

    private static DateTimeOffset Min(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTimeOffset Max(
        DateTimeOffset left,
        DateTimeOffset right) =>
        left >= right ? left : right;
}
