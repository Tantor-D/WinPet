namespace WinPet.Core.Sessions;

public sealed record WorkSessionSettings
{
    public TimeSpan ActiveInputWindow { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaximumWorkDuration { get; init; } = TimeSpan.FromMinutes(45);

    public TimeSpan WarningLeadTime { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan QualifiedBreakDuration { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (ActiveInputWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ActiveInputWindow),
                "The active input window must be positive.");
        }

        if (MaximumWorkDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumWorkDuration),
                "The maximum work duration must be positive.");
        }

        if (WarningLeadTime < TimeSpan.Zero || WarningLeadTime >= MaximumWorkDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WarningLeadTime),
                "The warning lead time must be non-negative and shorter than the maximum work duration.");
        }

        if (QualifiedBreakDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QualifiedBreakDuration),
                "The qualified break duration must be positive.");
        }
    }
}
