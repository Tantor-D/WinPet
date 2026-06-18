using System.Runtime.CompilerServices;
using WinPet.Core.Activity;
using WinPet.Core.History;
using WinPet.Core.Sessions;
using WinPet.Desktop.Services;

namespace WinPet.Desktop.Tests.Services;

public sealed class ActivityTrackingServiceTests
{
    [Fact]
    public async Task Clear_data_publishes_reset_state_and_empty_statistics()
    {
        var history = new EmptyHistoryStore();
        await using var service = new ActivityTrackingService(
            new EmptyActivityMonitor(),
            new WorkSessionEngine(new WorkSessionSettings()),
            history);
        WorkSessionUpdate? update = null;
        DailyActivitySummary? summary = null;
        IReadOnlyList<HourlyActivitySummary>? timeline = null;
        IReadOnlyList<DailyTrendPoint>? trend = null;
        service.Updated += (_, value) => update = value;
        service.TodaySummaryUpdated += (_, value) => summary = value;
        service.TodayTimelineUpdated += (_, value) => timeline = value;
        service.TrendUpdated += (_, value) => trend = value;

        await service.ClearDataAsync();

        Assert.True(history.WasCleared);
        Assert.NotNull(update);
        Assert.Equal(WorkSessionState.Working, update.State);
        Assert.NotNull(summary);
        Assert.Equal(TimeSpan.Zero, summary.ActiveDuration);
        Assert.NotNull(timeline);
        Assert.Equal(24, timeline.Count);
        Assert.NotNull(trend);
        Assert.Equal(14, trend.Count);
    }

    private sealed class EmptyActivityMonitor : IActivityMonitor
    {
        public async IAsyncEnumerable<ActivitySnapshot> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken =
                default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyHistoryStore :
        IActivityHistoryStore,
        IActivityDataManager
    {
        public bool WasCleared { get; private set; }

        public Task InitializeAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordAsync(
            ActivitySnapshot snapshot,
            WorkSessionUpdate session,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordManualResetAsync(
            DateTimeOffset timestamp,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordApplicationStoppedAsync(
            DateTimeOffset timestamp,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<DailyActivitySummary> GetDailySummaryAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DailyActivitySummary(
                localDate,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                0,
                0,
                TimeSpan.Zero,
                0));

        public Task<IReadOnlyList<HourlyActivitySummary>>
            GetHourlyActivityAsync(
                DateOnly localDate,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HourlyActivitySummary>>(
                Enumerable.Range(0, 24)
                    .Select(hour => new HourlyActivitySummary(
                        hour,
                        TimeSpan.Zero,
                        TimeSpan.Zero,
                        TimeSpan.Zero))
                    .ToArray());

        public Task<IReadOnlyList<DailyTrendPoint>> GetDailyTrendAsync(
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyTrendPoint>>(
                Enumerable.Range(0, endDate.DayNumber - startDate.DayNumber + 1)
                    .Select(offset => new DailyTrendPoint(
                        startDate.AddDays(offset),
                        TimeSpan.Zero,
                        TimeSpan.Zero,
                        TimeSpan.Zero,
                        0))
                    .ToArray());

        public Task<string> ExportCsvArchiveAsync(
            string outputDirectory,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public Task ClearAllAsync(
            CancellationToken cancellationToken = default)
        {
            WasCleared = true;
            return Task.CompletedTask;
        }
    }
}
