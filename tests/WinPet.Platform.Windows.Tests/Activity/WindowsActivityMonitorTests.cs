using WinPet.Platform.Windows.Activity;

namespace WinPet.Platform.Windows.Tests.Activity;

public sealed class WindowsActivityMonitorTests
{
    [Fact]
    public async Task Monitor_emits_a_real_activity_snapshot_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var monitor = new WindowsActivityMonitor(
            new WindowsActivityMonitorOptions
            {
                SamplingInterval = TimeSpan.FromMilliseconds(250),
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var snapshots =
            monitor.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        var hasSnapshot = await snapshots.MoveNextAsync();

        Assert.True(hasSnapshot);
        Assert.True(snapshots.Current.IdleDuration >= TimeSpan.Zero);
        Assert.True(
            DateTimeOffset.UtcNow - snapshots.Current.Timestamp <
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Sampling_interval_rejects_excessive_polling()
    {
        var options = new WindowsActivityMonitorOptions
        {
            SamplingInterval = TimeSpan.FromMilliseconds(100),
        };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}
