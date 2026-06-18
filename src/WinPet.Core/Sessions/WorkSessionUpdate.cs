namespace WinPet.Core.Sessions;

public sealed record WorkSessionUpdate(
    DateTimeOffset Timestamp,
    WorkSessionState State,
    TimeSpan ContinuousWorkDuration,
    TimeSpan ActiveDuration,
    TimeSpan CurrentBreakDuration,
    TimeSpan OvertimeDuration,
    bool QualifiedBreakCompleted);
