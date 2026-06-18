using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WinPet.Core.Sessions;
using WinPet.Desktop.Services;
using WinPet.Desktop.ViewModels;
using WinPet.Desktop.Views;
using WinPet.Infrastructure.History;
using WinPet.Platform.Windows.Activity;

namespace WinPet.Desktop;

public partial class App : Application
{
    private ActivityTrackingService? _trackingService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = CreateMainWindowViewModel(),
            };

            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindowViewModel CreateMainWindowViewModel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new MainWindowViewModel();
        }

        var settings = new WorkSessionSettings();
        var monitor = new WindowsActivityMonitor();
        var engine = new WorkSessionEngine(settings);
        var databasePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "WinPet",
            "winpet.db");
        var historyStore = new SqliteActivityHistoryStore(
            databasePath,
            settings);
        historyStore.InitializeAsync().GetAwaiter().GetResult();
        _trackingService = new ActivityTrackingService(
            monitor,
            engine,
            historyStore);
        return new MainWindowViewModel(_trackingService);
    }

    private void OnShutdownRequested(
        object? sender,
        ShutdownRequestedEventArgs args)
    {
        if (_trackingService is null)
        {
            return;
        }

        _trackingService.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
