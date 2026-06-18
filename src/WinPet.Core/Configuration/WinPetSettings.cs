using WinPet.Core.Sessions;

namespace WinPet.Core.Configuration;

public sealed record WinPetSettings
{
    public int MaximumWorkMinutes { get; init; } = 45;

    public int WarningLeadMinutes { get; init; } = 5;

    public int QualifiedBreakMinutes { get; init; } = 5;

    public int ActiveInputSeconds { get; init; } = 60;

    public bool PetEnabled { get; init; } = true;

    public bool PetAlwaysOnTop { get; init; } = true;

    public bool NotificationsEnabled { get; init; } = true;

    public bool StartMinimized { get; init; }

    public bool LaunchAtStartup { get; init; }

    public string ThemeName { get; init; } = "default";

    public WorkSessionSettings ToWorkSessionSettings()
    {
        Validate();
        return new WorkSessionSettings
        {
            MaximumWorkDuration = TimeSpan.FromMinutes(MaximumWorkMinutes),
            WarningLeadTime = TimeSpan.FromMinutes(WarningLeadMinutes),
            QualifiedBreakDuration =
                TimeSpan.FromMinutes(QualifiedBreakMinutes),
            ActiveInputWindow = TimeSpan.FromSeconds(ActiveInputSeconds),
        };
    }

    public void Validate()
    {
        if (MaximumWorkMinutes is < 5 or > 240)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumWorkMinutes));
        }

        if (WarningLeadMinutes < 0 ||
            WarningLeadMinutes >= MaximumWorkMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(WarningLeadMinutes));
        }

        if (QualifiedBreakMinutes is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QualifiedBreakMinutes));
        }

        if (ActiveInputSeconds is < 10 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(ActiveInputSeconds));
        }
    }
}
