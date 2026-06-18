using System.Runtime.Versioning;
using WinPet.Core.Activity;

namespace WinPet.Platform.Windows.Activity;

[SupportedOSPlatform("windows")]
public sealed class WindowsActivityMonitor : IActivityMonitor, IDisposable
{
    private readonly WindowsActivityMonitorOptions _options;
    private readonly WindowsSessionStateTracker _sessionState;
    private bool _disposed;

    public WindowsActivityMonitor(WindowsActivityMonitorOptions? options = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "WindowsActivityMonitor can only run on Windows.");
        }

        _options = options ?? new WindowsActivityMonitorOptions();
        _options.Validate();
        _sessionState = new WindowsSessionStateTracker();
    }

    public async IAsyncEnumerable<ActivitySnapshot> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        yield return Capture();

        using var timer = new PeriodicTimer(_options.SamplingInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            yield return Capture();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _sessionState.Dispose();
        _disposed = true;
    }

    private ActivitySnapshot Capture() =>
        new(
            DateTimeOffset.UtcNow,
            WindowsLastInputReader.GetIdleDuration(),
            _sessionState.IsSessionLocked,
            _sessionState.IsSystemSuspended);
}
