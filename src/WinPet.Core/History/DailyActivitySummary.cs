namespace WinPet.Core.History;

public sealed record DailyActivitySummary(
    DateOnly LocalDate,
    TimeSpan ActiveDuration,
    TimeSpan IdleDuration,
    TimeSpan LockedDuration,
    TimeSpan SuspendedDuration,
    TimeSpan OvertimeDuration)
{
    public TimeSpan ComputerDuration => ActiveDuration + IdleDuration;
}
