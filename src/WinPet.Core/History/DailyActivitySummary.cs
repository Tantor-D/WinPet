namespace WinPet.Core.History;

public sealed record DailyActivitySummary(
    DateOnly LocalDate,
    TimeSpan ActiveDuration,
    TimeSpan IdleDuration,
    TimeSpan LockedDuration,
    TimeSpan SuspendedDuration,
    TimeSpan OvertimeDuration,
    int CompletedWorkSessions,
    int QualifiedBreaks,
    TimeSpan LongestWorkSession,
    int ReminderCount)
{
    public TimeSpan ComputerDuration => ActiveDuration + IdleDuration;
}
