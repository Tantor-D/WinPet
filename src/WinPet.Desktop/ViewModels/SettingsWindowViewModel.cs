using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPet.Core.Configuration;
using WinPet.Core.Pets;
using WinPet.Core.Platform;
using WinPet.Desktop.Services;

namespace WinPet.Desktop.ViewModels;

public partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly ISettingsStore _settingsStore;
    private readonly ActivityTrackingService _trackingService;
    private readonly IAutoStartService? _autoStartService;

    [ObservableProperty]
    private int _maximumWorkMinutes;

    [ObservableProperty]
    private int _warningLeadMinutes;

    [ObservableProperty]
    private int _qualifiedBreakMinutes;

    [ObservableProperty]
    private int _activeInputSeconds;

    [ObservableProperty]
    private bool _petEnabled;

    [ObservableProperty]
    private bool _petAlwaysOnTop;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _themeName = string.Empty;

    public IReadOnlyList<CodexPetDefinition> AvailablePets { get; }

    public SettingsWindowViewModel(
        WinPetSettings settings,
        ISettingsStore settingsStore,
        ActivityTrackingService trackingService,
        IReadOnlyList<CodexPetDefinition> availablePets,
        IAutoStartService? autoStartService)
    {
        _settingsStore = settingsStore;
        _trackingService = trackingService;
        AvailablePets = availablePets;
        _autoStartService = autoStartService;
        Load(settings);
    }

    public event EventHandler<WinPetSettings>? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var settings = CreateSettings();
            settings.Validate();
            await _settingsStore.SaveAsync(settings);
            _trackingService.UpdateSettings(settings);
            _autoStartService?.SetEnabled(settings.LaunchAtStartup);
            StatusText = "已保存并立即应用";
            Saved?.Invoke(this, settings);
        }
        catch (ArgumentOutOfRangeException)
        {
            StatusText = "设置值超出允许范围";
        }
    }

    private WinPetSettings CreateSettings() =>
        new()
        {
            MaximumWorkMinutes = MaximumWorkMinutes,
            WarningLeadMinutes = WarningLeadMinutes,
            QualifiedBreakMinutes = QualifiedBreakMinutes,
            ActiveInputSeconds = ActiveInputSeconds,
            PetEnabled = PetEnabled,
            PetAlwaysOnTop = PetAlwaysOnTop,
            NotificationsEnabled = NotificationsEnabled,
            StartMinimized = StartMinimized,
            LaunchAtStartup = LaunchAtStartup,
            ThemeName = ThemeName,
        };

    private void Load(WinPetSettings settings)
    {
        MaximumWorkMinutes = settings.MaximumWorkMinutes;
        WarningLeadMinutes = settings.WarningLeadMinutes;
        QualifiedBreakMinutes = settings.QualifiedBreakMinutes;
        ActiveInputSeconds = settings.ActiveInputSeconds;
        PetEnabled = settings.PetEnabled;
        PetAlwaysOnTop = settings.PetAlwaysOnTop;
        NotificationsEnabled = settings.NotificationsEnabled;
        StartMinimized = settings.StartMinimized;
        LaunchAtStartup = _autoStartService?.IsEnabled()
            ?? settings.LaunchAtStartup;
        ThemeName = settings.ThemeName;
    }
}
