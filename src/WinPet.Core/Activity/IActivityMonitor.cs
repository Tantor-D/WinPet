namespace WinPet.Core.Activity;

public interface IActivityMonitor
{
    IAsyncEnumerable<ActivitySnapshot> WatchAsync(
        CancellationToken cancellationToken = default);
}
