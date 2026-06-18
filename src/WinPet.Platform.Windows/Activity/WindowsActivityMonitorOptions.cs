namespace WinPet.Platform.Windows.Activity;

public sealed record WindowsActivityMonitorOptions
{
    public TimeSpan SamplingInterval { get; init; } = TimeSpan.FromSeconds(5);

    public void Validate()
    {
        if (SamplingInterval < TimeSpan.FromMilliseconds(250))
        {
            throw new ArgumentOutOfRangeException(
                nameof(SamplingInterval),
                "The sampling interval must be at least 250 milliseconds.");
        }
    }
}
