using WinPet.Core.Activity;
using WinPet.Core.Sessions;
using WinPet.Infrastructure.History;

namespace WinPet.Infrastructure.Tests.History;

public sealed class SqliteActivityHistoryStoreTests
{
    [Fact]
    public async Task Records_active_and_idle_time_into_daily_summary()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var settings = new WorkSessionSettings();
            var store = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await store.InitializeAsync();

            var start = new DateTimeOffset(
                2026,
                6,
                18,
                8,
                0,
                0,
                TimeSpan.Zero);
            await store.RecordAsync(
                new ActivitySnapshot(start, TimeSpan.Zero),
                Update(start, WorkSessionState.Working));
            await store.RecordAsync(
                new ActivitySnapshot(
                    start.AddSeconds(30),
                    TimeSpan.Zero),
                Update(start.AddSeconds(30), WorkSessionState.Working));
            await store.RecordAsync(
                new ActivitySnapshot(
                    start.AddMinutes(1),
                    TimeSpan.FromMinutes(2)),
                Update(start.AddMinutes(1), WorkSessionState.Idle));

            var localDate = DateOnly.FromDateTime(start.ToLocalTime().DateTime);
            var summary = await store.GetDailySummaryAsync(localDate);

            Assert.Equal(TimeSpan.FromSeconds(30), summary.ActiveDuration);
            Assert.Equal(TimeSpan.FromSeconds(30), summary.IdleDuration);
            Assert.Equal(TimeSpan.FromMinutes(1), summary.ComputerDuration);

            var timeline = await store.GetHourlyActivityAsync(localDate);
            Assert.Equal(24, timeline.Count);
            Assert.Equal(
                TimeSpan.FromSeconds(30),
                timeline.Aggregate(
                    TimeSpan.Zero,
                    (total, point) => total + point.ActiveDuration));
            Assert.Equal(
                TimeSpan.FromSeconds(30),
                timeline.Aggregate(
                    TimeSpan.Zero,
                    (total, point) => total + point.IdleDuration));

            var trend = await store.GetDailyTrendAsync(
                localDate.AddDays(-1),
                localDate);
            Assert.Equal(2, trend.Count);
            Assert.Equal(
                TimeSpan.FromSeconds(30),
                trend[^1].ActiveDuration);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Data_can_be_read_by_a_new_store_instance()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var settings = new WorkSessionSettings();
            var start = DateTimeOffset.Now;
            var writer = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await writer.InitializeAsync();
            await writer.RecordAsync(
                new ActivitySnapshot(start, TimeSpan.Zero),
                Update(start, WorkSessionState.Working));
            await writer.RecordAsync(
                new ActivitySnapshot(
                    start.AddSeconds(20),
                    TimeSpan.Zero),
                Update(start.AddSeconds(20), WorkSessionState.Working));

            var reader = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await reader.InitializeAsync();
            var summary = await reader.GetDailySummaryAsync(
                DateOnly.FromDateTime(start.LocalDateTime));

            Assert.Equal(TimeSpan.FromSeconds(20), summary.ActiveDuration);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Qualified_break_closes_work_and_records_rest_session()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var settings = new WorkSessionSettings();
            var store = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await store.InitializeAsync();
            var engine = new WorkSessionEngine(settings);
            var start = new DateTimeOffset(
                2026,
                6,
                18,
                9,
                0,
                0,
                TimeSpan.Zero);

            await RecordAsync(
                store,
                engine,
                new ActivitySnapshot(start, TimeSpan.Zero));
            for (var minute = 5; minute <= 30; minute += 5)
            {
                await RecordAsync(
                    store,
                    engine,
                    new ActivitySnapshot(
                        start.AddMinutes(minute),
                        TimeSpan.Zero));
            }
            for (var minute = 31; minute <= 35; minute++)
            {
                await RecordAsync(
                    store,
                    engine,
                    new ActivitySnapshot(
                        start.AddMinutes(minute),
                        TimeSpan.FromMinutes(minute - 30)));
            }
            await RecordAsync(
                store,
                engine,
                new ActivitySnapshot(
                    start.AddMinutes(36),
                    TimeSpan.Zero));

            var summary = await store.GetDailySummaryAsync(
                DateOnly.FromDateTime(start.ToLocalTime().DateTime));

            Assert.Equal(1, summary.CompletedWorkSessions);
            Assert.Equal(1, summary.QualifiedBreaks);
            Assert.Equal(
                TimeSpan.FromMinutes(30),
                summary.LongestWorkSession);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Manual_reset_finishes_current_work_session()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var settings = new WorkSessionSettings();
            var store = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await store.InitializeAsync();
            var start = DateTimeOffset.Now;

            await store.RecordAsync(
                new ActivitySnapshot(start, TimeSpan.Zero),
                Update(start, WorkSessionState.Working));
            for (var minute = 5; minute <= 20; minute += 5)
            {
                await store.RecordAsync(
                    new ActivitySnapshot(
                        start.AddMinutes(minute),
                        TimeSpan.Zero),
                    Update(
                        start.AddMinutes(minute),
                        WorkSessionState.Working));
            }
            await store.RecordManualResetAsync(start.AddMinutes(20));

            var summary = await store.GetDailySummaryAsync(
                DateOnly.FromDateTime(start.LocalDateTime));

            Assert.Equal(1, summary.CompletedWorkSessions);
            Assert.Equal(
                TimeSpan.FromMinutes(20),
                summary.LongestWorkSession);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Warning_and_break_due_transitions_are_counted_as_reminders()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var settings = new WorkSessionSettings
            {
                MaximumWorkDuration = TimeSpan.FromMinutes(10),
                WarningLeadTime = TimeSpan.FromMinutes(2),
            };
            var store = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await store.InitializeAsync();
            var engine = new WorkSessionEngine(settings);
            var start = DateTimeOffset.Now;

            await RecordAsync(
                store,
                engine,
                new ActivitySnapshot(start, TimeSpan.Zero));
            await RecordAsync(
                store,
                engine,
                new ActivitySnapshot(
                    start.AddMinutes(8),
                    TimeSpan.Zero));
            await RecordAsync(
                store,
                engine,
                new ActivitySnapshot(
                    start.AddMinutes(10),
                    TimeSpan.Zero));

            var summary = await store.GetDailySummaryAsync(
                DateOnly.FromDateTime(start.LocalDateTime));

            Assert.Equal(2, summary.ReminderCount);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task Exports_all_history_tables_and_can_clear_data()
    {
        var databasePath = CreateDatabasePath();
        var exportDirectory = Path.Combine(
            Path.GetTempPath(),
            "WinPet.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new WorkSessionSettings();
            var store = new SqliteActivityHistoryStore(
                databasePath,
                settings);
            await store.InitializeAsync();
            var start = DateTimeOffset.Now;
            await store.RecordAsync(
                new ActivitySnapshot(start, TimeSpan.Zero),
                Update(start, WorkSessionState.Working));
            await store.RecordAsync(
                new ActivitySnapshot(start.AddSeconds(10), TimeSpan.Zero),
                Update(start.AddSeconds(10), WorkSessionState.Working));

            var archive = await store.ExportCsvArchiveAsync(exportDirectory);
            Assert.True(File.Exists(archive));
            using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            {
                Assert.Contains(
                    zip.Entries,
                    entry => entry.Name == "activity_buckets.csv");
                Assert.Contains(
                    zip.Entries,
                    entry => entry.Name == "work_sessions.csv");
            }

            await store.ClearAllAsync();
            var summary = await store.GetDailySummaryAsync(
                DateOnly.FromDateTime(start.LocalDateTime));
            Assert.Equal(TimeSpan.Zero, summary.ActiveDuration);
        }
        finally
        {
            DeleteDatabase(databasePath);
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
    }

    private static WorkSessionUpdate Update(
        DateTimeOffset timestamp,
        WorkSessionState state) =>
        new(
            timestamp,
            state,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            QualifiedBreakCompleted: false);

    private static async Task RecordAsync(
        SqliteActivityHistoryStore store,
        WorkSessionEngine engine,
        ActivitySnapshot snapshot)
    {
        var update = engine.Process(snapshot);
        await store.RecordAsync(snapshot, update);
    }

    private static string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "WinPet.Tests",
            Guid.NewGuid().ToString("N"));
        return Path.Combine(directory, "history.db");
    }

    private static void DeleteDatabase(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
