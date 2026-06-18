using WinPet.Core.Activity;
using WinPet.Core.Configuration;
using WinPet.Core.History;
using WinPet.Core.Sessions;
using WinPet.Infrastructure.History;

namespace WinPet.Desktop.Services;

public sealed class ActivityTrackingService : IAsyncDisposable
{
    private readonly IActivityMonitor _activityMonitor;
    private readonly WorkSessionEngine _sessionEngine;
    private readonly IActivityHistoryStore? _historyStore;
    private readonly IActivityDataManager? _dataManager;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _monitoringTask;
    private bool _isPaused;
    private DateTimeOffset? _lastStatisticsRefresh;
    private DateTimeOffset? _remindersSnoozedUntil;

    public ActivityTrackingService(
        IActivityMonitor activityMonitor,
        WorkSessionEngine sessionEngine,
        IActivityHistoryStore? historyStore = null)
    {
        _activityMonitor = activityMonitor;
        _sessionEngine = sessionEngine;
        _historyStore = historyStore;
        _dataManager = historyStore as IActivityDataManager;
    }

    public event EventHandler<WorkSessionUpdate>? Updated;

    public event EventHandler<DailyActivitySummary>? TodaySummaryUpdated;

    public event EventHandler<IReadOnlyList<HourlyActivitySummary>>?
        TodayTimelineUpdated;

    public event EventHandler<IReadOnlyList<DailyTrendPoint>>?
        TrendUpdated;

    public event EventHandler<WorkSessionSettings>? SettingsUpdated;

    public void Start()
    {
        _monitoringTask ??= MonitorAsync(_stopping.Token);
    }

    public bool IsPaused => _isPaused;

    public WorkSessionUpdate TogglePause()
    {
        _isPaused = !_isPaused;
        var update = _sessionEngine.SetPaused(
            _isPaused,
            DateTimeOffset.UtcNow);
        Updated?.Invoke(this, update);
        return update;
    }

    public void SnoozeReminders(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _remindersSnoozedUntil = DateTimeOffset.UtcNow + duration;
    }

    public bool AreRemindersSnoozed(DateTimeOffset timestamp) =>
        _remindersSnoozedUntil is { } until && timestamp < until;

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

    public void UpdateSettings(WinPetSettings settings)
    {
        var workSettings = settings.ToWorkSessionSettings();
        _sessionEngine.UpdateSettings(workSettings);
        if (_historyStore is SqliteActivityHistoryStore sqliteStore)
        {
            sqliteStore.UpdateSettings(workSettings);
        }

        SettingsUpdated?.Invoke(this, workSettings);
    }

    public Task<string> ExportDataAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dataManager is null)
        {
            throw new InvalidOperationException(
                "Data export is not available.");
        }

        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "WinPet Exports");
        return _dataManager.ExportCsvArchiveAsync(
            outputDirectory,
            cancellationToken);
    }

    public async Task ClearDataAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dataManager is null)
        {
            return;
        }

        await _dataManager.ClearAllAsync(cancellationToken)
            .ConfigureAwait(false);
        var timestamp = DateTimeOffset.UtcNow;
        var update = _sessionEngine.Reset(timestamp);
        _lastStatisticsRefresh = null;
        Updated?.Invoke(this, update);
        await RefreshStatisticsAsync(timestamp, cancellationToken)
            .ConfigureAwait(false);
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
                if (_lastStatisticsRefresh is null ||
                    snapshot.Timestamp - _lastStatisticsRefresh >=
                    TimeSpan.FromSeconds(30))
                {
                    await RefreshStatisticsAsync(
                        snapshot.Timestamp,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            Updated?.Invoke(this, update);
        }
    }

    private async Task RefreshStatisticsAsync(
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (_historyStore is null)
        {
            return;
        }

        var localDate = DateOnly.FromDateTime(
            timestamp.ToLocalTime().DateTime);
        var today = await _historyStore.GetDailySummaryAsync(
            localDate,
            cancellationToken).ConfigureAwait(false);
        TodaySummaryUpdated?.Invoke(this, today);

        var timeline = await _historyStore.GetHourlyActivityAsync(
            localDate,
            cancellationToken).ConfigureAwait(false);
        TodayTimelineUpdated?.Invoke(this, timeline);

        var trend = await _historyStore.GetDailyTrendAsync(
            localDate.AddDays(-13),
            localDate,
            cancellationToken).ConfigureAwait(false);
        TrendUpdated?.Invoke(this, trend);
        _lastStatisticsRefresh = timestamp;
    }
}
