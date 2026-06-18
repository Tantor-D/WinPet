using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using WinPet.Core.Configuration;
using WinPet.Core.Pets;
using WinPet.Core.Sessions;
using WinPet.Core.Platform;
using WinPet.Desktop.Services;
using WinPet.Desktop.ViewModels;
using WinPet.Desktop.Views;
using WinPet.Infrastructure.Configuration;
using WinPet.Infrastructure.History;
using WinPet.Infrastructure.Pets;
using WinPet.Platform.Windows.Activity;
using WinPet.Platform.Windows.Startup;
using WinPet.Platform.Windows.Notifications;

namespace WinPet.Desktop;

public partial class App : Application
{
    private ActivityTrackingService? _trackingService;
    private ISettingsStore? _settingsStore;
    private WinPetSettings _settings = new();
    private MainWindow? _mainWindow;
    private PetWindow? _petWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private ReminderNotificationService? _notificationService;
    private ICodexPetCatalog? _petCatalog;
    private PetWindowViewModel? _petViewModel;
    private IAutoStartService? _autoStartService;
    private ISystemNotificationService? _systemNotificationService;
    private bool _isExiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            InitializeServices();
            InitializeWindows(desktop);
            InitializeTray(desktop);
            if (desktop.Args?.Contains(
                    "--settings",
                    StringComparer.OrdinalIgnoreCase) == true)
            {
                ShowSettings();
            }
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeServices()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "WinPet");
        _settingsStore = new JsonSettingsStore(
            Path.Combine(dataDirectory, "settings.json"));
        _settings = _settingsStore.LoadAsync().GetAwaiter().GetResult();
        _petCatalog = new CodexPetCatalog();
        var availablePets = _petCatalog.Discover();
        if (availablePets.Count > 0 &&
            _petCatalog.Find(_settings.ThemeName) is null)
        {
            _settings = _settings with
            {
                ThemeName = availablePets[0].Manifest.Id,
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            _autoStartService = new WindowsAutoStartService(
                Environment.ProcessPath);
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            _systemNotificationService =
                new WindowsSystemNotificationService();
        }

        var workSettings = _settings.ToWorkSessionSettings();
        var monitor = new WindowsActivityMonitor();
        var engine = new WorkSessionEngine(workSettings);
        var historyStore = new SqliteActivityHistoryStore(
            Path.Combine(dataDirectory, "winpet.db"),
            workSettings);
        historyStore.InitializeAsync().GetAwaiter().GetResult();
        _trackingService = new ActivityTrackingService(
            monitor,
            engine,
            historyStore);
    }

    private void InitializeWindows(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var mainViewModel = _trackingService is null
            ? new MainWindowViewModel()
            : new MainWindowViewModel(_trackingService);
        mainViewModel.OpenSettingsRequested += (_, _) => ShowSettings();
        mainViewModel.TogglePetRequested += (_, _) => TogglePet();
        mainViewModel.ClearDataRequested += async (_, _) =>
            await ConfirmClearDataAsync(mainViewModel);

        _mainWindow = new MainWindow
        {
            DataContext = mainViewModel,
        };
        _mainWindow.Closing += OnMainWindowClosing;
        _mainWindow.Opened += (_, _) =>
        {
            if (_trackingService is not null)
            {
                _notificationService = new ReminderNotificationService(
                    _mainWindow,
                    _trackingService,
                    _settings,
                    _systemNotificationService);
            }
        };
        desktop.MainWindow = _mainWindow;

        if (_trackingService is not null)
        {
            _petViewModel = new PetWindowViewModel(
                _trackingService,
                _petCatalog?.Find(_settings.ThemeName));
            _petWindow = new PetWindow
            {
                DataContext = _petViewModel,
                Topmost = _settings.PetAlwaysOnTop,
            };
            _petWindow.BubbleToggled += (_, _) =>
                _petViewModel.IsBubbleVisible =
                    !_petViewModel.IsBubbleVisible;
            _petWindow.Opened += (_, _) => PositionPetAtBottomRight();
            if (_settings.PetEnabled)
            {
                _petWindow.Show();
            }
        }

        if (_settings.StartMinimized)
        {
            _mainWindow.Hide();
        }
    }

    private void InitializeTray(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var showItem = new NativeMenuItem("显示统计面板");
        showItem.Click += (_, _) => ShowMainWindow();
        var petItem = new NativeMenuItem("显示 / 隐藏桌宠");
        petItem.Click += (_, _) => TogglePet();
        var pauseItem = new NativeMenuItem("暂停 / 继续计时");
        pauseItem.Click += (_, _) => _trackingService?.TogglePause();
        var resetItem = new NativeMenuItem("重置本轮计时");
        resetItem.Click += async (_, _) =>
        {
            if (_trackingService is not null)
            {
                await _trackingService.ResetAsync();
            }
        };
        var snoozeMenu = new NativeMenuItem("延后提醒")
        {
            Menu = new NativeMenu
            {
                Items =
                {
                    CreateSnoozeItem("5 分钟", 5),
                    CreateSnoozeItem("10 分钟", 10),
                    CreateSnoozeItem("15 分钟", 15),
                },
            },
        };
        var settingsItem = new NativeMenuItem("设置");
        settingsItem.Click += (_, _) => ShowSettings();
        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            desktop.Shutdown();
        };

        var menu = new NativeMenu
        {
            Items =
            {
                showItem,
                petItem,
                new NativeMenuItemSeparator(),
                pauseItem,
                resetItem,
                snoozeMenu,
                settingsItem,
                new NativeMenuItemSeparator(),
                exitItem,
            },
        };

        using var iconStream = AssetLoader.Open(
            new Uri("avares://WinPet/Assets/winpet-icon.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "WinPet",
            Menu = menu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
    }

    private NativeMenuItem CreateSnoozeItem(string label, int minutes)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) =>
            _trackingService?.SnoozeReminders(TimeSpan.FromMinutes(minutes));
        return item;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void TogglePet()
    {
        if (_petWindow is null)
        {
            return;
        }

        if (_petWindow.IsVisible)
        {
            _petWindow.Hide();
        }
        else
        {
            _petWindow.Show();
            PositionPetAtBottomRight();
        }
    }

    private void ShowSettings()
    {
        if (_settingsStore is null || _trackingService is null)
        {
            return;
        }

        if (_settingsWindow?.IsVisible == true)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsWindowViewModel(
            _settings,
            _settingsStore,
            _trackingService,
            _petCatalog?.Discover() ?? [],
            _autoStartService);
        viewModel.Saved += (_, settings) =>
        {
            _settings = settings;
            ApplySettings();
        };
        _settingsWindow = new SettingsWindow
        {
            DataContext = viewModel,
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (_mainWindow is not null)
        {
            _settingsWindow.Show(_mainWindow);
        }
        else
        {
            _settingsWindow.Show();
        }
    }

    private void ApplySettings()
    {
        if (_petWindow is not null)
        {
            _petWindow.Topmost = _settings.PetAlwaysOnTop;
            if (_settings.PetEnabled && !_petWindow.IsVisible)
            {
                _petWindow.Show();
            }
            else if (!_settings.PetEnabled && _petWindow.IsVisible)
            {
                _petWindow.Hide();
            }
        }

        _notificationService?.ApplySettings(_settings);
        _petViewModel?.ApplyPet(_petCatalog?.Find(_settings.ThemeName));
    }

    private void PositionPetAtBottomRight()
    {
        if (_petWindow?.Screens.Primary is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        _petWindow.Position = new PixelPoint(
            area.Right - (int)_petWindow.Width - 24,
            area.Bottom - (int)_petWindow.Height - 24);
    }

    private async Task ConfirmClearDataAsync(
        MainWindowViewModel viewModel)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var dialog = new ConfirmationWindow();
        var confirmed = await dialog.ShowDialog<bool>(_mainWindow);
        if (confirmed)
        {
            await viewModel.ClearDataAsync();
        }
    }

    private void OnMainWindowClosing(
        object? sender,
        WindowClosingEventArgs args)
    {
        if (_isExiting)
        {
            return;
        }

        args.Cancel = true;
        _mainWindow?.Hide();
    }

    private void OnShutdownRequested(
        object? sender,
        ShutdownRequestedEventArgs args)
    {
        _trayIcon?.Dispose();
        _systemNotificationService?.Dispose();
        _trackingService?.DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}
