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
