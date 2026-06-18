using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPet.Core.Sessions;
using WinPet.Desktop.Services;

namespace WinPet.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ActivityTrackingService? _trackingService;

    [ObservableProperty]
    private string _stateText = "正在初始化活动检测…";

    [ObservableProperty]
    private string _continuousWorkText = "00:00:00";

    [ObservableProperty]
    private string _activeTimeText = "00:00:00";

    [ObservableProperty]
    private string _breakTimeText = "00:00:00";

    [ObservableProperty]
    private string _overtimeText = "00:00:00";

    [ObservableProperty]
    private string _lastUpdatedText = "等待首次采样";

    public MainWindowViewModel()
    {
    }

    public MainWindowViewModel(ActivityTrackingService trackingService)
    {
        _trackingService = trackingService;
        _trackingService.Updated += OnTrackingUpdated;
        _trackingService.Start();
    }

    [RelayCommand]
    private void ResetSession()
    {
        if (_trackingService is null)
        {
            return;
        }

        ApplyUpdate(_trackingService.Reset());
    }

    private void OnTrackingUpdated(object? sender, WorkSessionUpdate update) =>
        Dispatcher.UIThread.Post(() => ApplyUpdate(update));

    private void ApplyUpdate(WorkSessionUpdate update)
    {
        StateText = update.State switch
        {
            WorkSessionState.Working => "工作中",
            WorkSessionState.Idle => "暂时离开",
            WorkSessionState.Warning => "快到休息时间了",
            WorkSessionState.BreakDue => "该休息了",
            WorkSessionState.Resting => "休息中",
            WorkSessionState.Paused => "已暂停",
            _ => update.State.ToString(),
        };

        ContinuousWorkText = FormatDuration(update.ContinuousWorkDuration);
        ActiveTimeText = FormatDuration(update.ActiveDuration);
        BreakTimeText = FormatDuration(update.CurrentBreakDuration);
        OvertimeText = FormatDuration(update.OvertimeDuration);
        LastUpdatedText = $"最后采样：{update.Timestamp.ToLocalTime():HH:mm:ss}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
}
