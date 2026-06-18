using WinPet.Core.Activity;
using WinPet.Core.History;
using WinPet.Core.Sessions;

namespace WinPet.Desktop.Services;

public sealed class ActivityTrackingService : IAsyncDisposable
{
    private readonly IActivityMonitor _activityMonitor;
    private readonly WorkSessionEngine _sessionEngine;
    private readonly IActivityHistoryStore? _historyStore;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _monitoringTask;

    public ActivityTrackingService(
        IActivityMonitor activityMonitor,
        WorkSessionEngine sessionEngine,
        IActivityHistoryStore? historyStore = null)
    {
        _activityMonitor = activityMonitor;
        _sessionEngine = sessionEngine;
        _historyStore = historyStore;
    }

    public event EventHandler<WorkSessionUpdate>? Updated;

    public event EventHandler<DailyActivitySummary>? TodaySummaryUpdated;

    public event EventHandler<IReadOnlyList<HourlyActivitySummary>>?
        TodayTimelineUpdated;

    public void Start()
    {
        _monitoringTask ??= MonitorAsync(_stopping.Token);
    }

    public async Task<WorkSessionUpdate> ResetAsync()
    {
        var timestamp = DateTimeOffset.UtcNow;
        if (_historyStore is not null)
        {
            await _historyStore.RecordManualResetAsync(timestamp)
                .ConfigureAwait(false);
        }

        var update = _sessionEngine.Reset(timestamp);
        Updated?.Invoke(this, update);
        return update;
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        if (_monitoringTask is not null)
        {
            try
            {
                await _monitoringTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_historyStore is not null)
        {
            await _historyStore.RecordApplicationStoppedAsync(
                DateTimeOffset.UtcNow).ConfigureAwait(false);
        }

        _stopping.Dispose();

        if (_activityMonitor is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        await foreach (var snapshot in
            _activityMonitor.WatchAsync(cancellationToken))
        {
            var update = _sessionEngine.Process(snapshot);
            if (_historyStore is not null)
            {
                await _historyStore.RecordAsync(
                    snapshot,
                    update,
                    cancellationToken).ConfigureAwait(false);
                var today = await _historyStore.GetDailySummaryAsync(
                    DateOnly.FromDateTime(
                        snapshot.Timestamp.ToLocalTime().DateTime),
                    cancellationToken).ConfigureAwait(false);
                TodaySummaryUpdated?.Invoke(this, today);
                var timeline = await _historyStore.GetHourlyActivityAsync(
                    today.LocalDate,
                    cancellationToken).ConfigureAwait(false);
                TodayTimelineUpdated?.Invoke(this, timeline);
            }

            Updated?.Invoke(this, update);
        }
    }
}
