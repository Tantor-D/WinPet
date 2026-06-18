using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WinPet.Core.Sessions;
using WinPet.Desktop.Services;
using WinPet.Desktop.ViewModels;
using WinPet.Desktop.Views;
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

        var monitor = new WindowsActivityMonitor();
        var engine = new WorkSessionEngine(new WorkSessionSettings());
        _trackingService = new ActivityTrackingService(monitor, engine);
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
