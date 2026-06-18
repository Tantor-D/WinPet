namespace WinPet.Core.History;

public sealed record DailyTrendPoint(
    DateOnly Date,
    TimeSpan ActiveDuration,
    TimeSpan ComputerDuration,
    TimeSpan OvertimeDuration,
    int QualifiedBreaks);
