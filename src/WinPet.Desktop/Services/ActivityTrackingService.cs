using WinPet.Core.Activity;
using WinPet.Core.Sessions;

namespace WinPet.Desktop.Services;

public sealed class ActivityTrackingService : IAsyncDisposable
{
    private readonly IActivityMonitor _activityMonitor;
    private readonly WorkSessionEngine _sessionEngine;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _monitoringTask;

    public ActivityTrackingService(
        IActivityMonitor activityMonitor,
        WorkSessionEngine sessionEngine)
    {
        _activityMonitor = activityMonitor;
        _sessionEngine = sessionEngine;
    }

    public event EventHandler<WorkSessionUpdate>? Updated;

    public void Start()
    {
        _monitoringTask ??= MonitorAsync(_stopping.Token);
    }

    public WorkSessionUpdate Reset()
    {
        var update = _sessionEngine.Reset(DateTimeOffset.UtcNow);
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
            Updated?.Invoke(this, update);
        }
    }
}
