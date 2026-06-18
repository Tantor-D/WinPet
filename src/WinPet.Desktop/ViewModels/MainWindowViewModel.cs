using Avalonia.Threading;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinPet.Core.History;
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

    [ObservableProperty]
    private string _todayComputerText = "00:00:00";

    [ObservableProperty]
    private string _todayActiveText = "00:00:00";

    [ObservableProperty]
    private string _todayLongestText = "00:00:00";

    [ObservableProperty]
    private string _todaySessionsText = "0";

    [ObservableProperty]
    private string _todayBreaksText = "0";

    [ObservableProperty]
    private string _todayOvertimeText = "00:00:00";

    [ObservableProperty]
    private string _todayRemindersText = "0";

    [ObservableProperty]
    private string _peakActivityText = "暂无数据";

    public ObservableCollection<HourlyActivityBarViewModel> HourlyActivityBars
    {
        get;
    } = [];

    public MainWindowViewModel()
    {
    }

    public MainWindowViewModel(ActivityTrackingService trackingService)
    {
        _trackingService = trackingService;
        _trackingService.Updated += OnTrackingUpdated;
        _trackingService.TodaySummaryUpdated += OnTodaySummaryUpdated;
        _trackingService.TodayTimelineUpdated += OnTodayTimelineUpdated;
        _trackingService.Start();
    }

    [RelayCommand]
    private async Task ResetSessionAsync()
    {
        if (_trackingService is null)
        {
            return;
        }

        ApplyUpdate(await _trackingService.ResetAsync());
    }

    private void OnTrackingUpdated(object? sender, WorkSessionUpdate update) =>
        Dispatcher.UIThread.Post(() => ApplyUpdate(update));

    private void OnTodaySummaryUpdated(
        object? sender,
        DailyActivitySummary summary) =>
        Dispatcher.UIThread.Post(() => ApplyTodaySummary(summary));

    private void OnTodayTimelineUpdated(
        object? sender,
        IReadOnlyList<HourlyActivitySummary> timeline) =>
        Dispatcher.UIThread.Post(() => ApplyTodayTimeline(timeline));

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

    private void ApplyTodaySummary(DailyActivitySummary summary)
    {
        TodayComputerText = FormatDuration(summary.ComputerDuration);
        TodayActiveText = FormatDuration(summary.ActiveDuration);
        TodayLongestText = FormatDuration(summary.LongestWorkSession);
        TodaySessionsText = summary.CompletedWorkSessions.ToString();
        TodayBreaksText = summary.QualifiedBreaks.ToString();
        TodayOvertimeText = FormatDuration(summary.OvertimeDuration);
        TodayRemindersText = summary.ReminderCount.ToString();
    }

    private void ApplyTodayTimeline(
        IReadOnlyList<HourlyActivitySummary> timeline)
    {
        var maximum = timeline.Max(
            point => point.ActiveDuration.TotalSeconds);
        HourlyActivityBars.Clear();

        foreach (var point in timeline)
        {
            var height = maximum <= 0
                ? 3
                : 3 + (97 * point.ActiveDuration.TotalSeconds / maximum);
            var label = point.Hour % 3 == 0
                ? point.Hour.ToString("00")
                : string.Empty;
            HourlyActivityBars.Add(
                new HourlyActivityBarViewModel(
                    label,
                    height,
                    $"{point.Hour:00}:00–{point.Hour + 1:00}:00 · " +
                    $"活跃 {FormatDuration(point.ActiveDuration)}"));
        }

        var peak = timeline
            .Where(point => point.ActiveDuration > TimeSpan.Zero)
            .OrderByDescending(point => point.ActiveDuration)
            .FirstOrDefault();
        PeakActivityText = peak is null
            ? "暂无数据"
            : $"{peak.Hour:00}:00–{peak.Hour + 1:00}:00";
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
}
