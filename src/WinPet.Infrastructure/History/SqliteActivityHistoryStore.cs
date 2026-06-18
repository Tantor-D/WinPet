using System.Globalization;
using Microsoft.Data.Sqlite;
using WinPet.Core.Activity;
using WinPet.Core.History;
using WinPet.Core.Sessions;

namespace WinPet.Infrastructure.History;

public sealed class SqliteActivityHistoryStore : IActivityHistoryStore
{
    private const int SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly WorkSessionSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastRecordedAt;

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
                _lastRecordedAt = snapshot.Timestamp;
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
                startedAt = segmentEnd;
            }

            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            _lastRecordedAt = endedAt;
        }
        finally
        {
            _gate.Release();
        }
    }

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
                    COALESCE(SUM(overtime_milliseconds), 0)
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
                TimeSpan.FromMilliseconds(reader.GetInt64(4)));
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
