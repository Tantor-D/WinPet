namespace WinPet.Core.Activity;

public sealed record ActivitySnapshot(
    DateTimeOffset Timestamp,
    TimeSpan IdleDuration,
    bool IsSessionLocked = false,
    bool IsSystemSuspended = false);
