namespace WinPet.Core.History;

public sealed record HourlyActivitySummary(
    int Hour,
    TimeSpan ActiveDuration,
    TimeSpan IdleDuration,
    TimeSpan OvertimeDuration);
