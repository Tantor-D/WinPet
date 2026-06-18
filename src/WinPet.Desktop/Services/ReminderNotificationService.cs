using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using WinPet.Core.Configuration;
using WinPet.Core.Platform;
using WinPet.Core.Sessions;

namespace WinPet.Desktop.Services;

public sealed class ReminderNotificationService
{
    private readonly WindowNotificationManager _manager;
    private readonly ISystemNotificationService? _systemNotifications;
    private readonly ActivityTrackingService _trackingService;
    private WorkSessionState? _lastState;
    private DateTimeOffset? _lastBreakDueNotification;
    private bool _enabled;

    public ReminderNotificationService(
        TopLevel host,
        ActivityTrackingService trackingService,
        WinPetSettings settings,
        ISystemNotificationService? systemNotifications)
    {
        _manager = new WindowNotificationManager(host)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 3,
        };
        _enabled = settings.NotificationsEnabled;
        _systemNotifications = systemNotifications;
        _trackingService = trackingService;
        trackingService.Updated += OnUpdated;
    }

    public void ApplySettings(WinPetSettings settings)
    {
        _enabled = settings.NotificationsEnabled;
    }

    private void OnUpdated(object? sender, WorkSessionUpdate update)
    {
        if (!_enabled || _trackingService.AreRemindersSnoozed(update.Timestamp))
        {
            _lastState = update.State;
            return;
        }

        var repeatedBreakDue =
            update.State == WorkSessionState.BreakDue &&
            _lastState == WorkSessionState.BreakDue &&
            (_lastBreakDueNotification is null ||
             update.Timestamp - _lastBreakDueNotification >=
             TimeSpan.FromMinutes(10));
        if (update.State == _lastState && !repeatedBreakDue)
        {
            return;
        }

        _lastState = update.State;
        var notification = update.State switch
        {
            WorkSessionState.Warning => new Notification(
                "WinPet 提醒",
                "再过几分钟就该休息了，记得收个尾。",
                NotificationType.Information,
                TimeSpan.FromSeconds(8)),
            WorkSessionState.BreakDue => new Notification(
                "该离开电脑休息啦",
                "站起来走一走、喝点水，让眼睛看看远处。",
                NotificationType.Warning,
                TimeSpan.FromSeconds(12)),
            WorkSessionState.Resting
                when update.QualifiedBreakCompleted => new Notification(
                    "休息开始",
                    "这次休息达到标准后，工作计时会自动重置。",
                    NotificationType.Success,
                    TimeSpan.FromSeconds(8)),
            _ => null,
        };

        if (notification is not null)
        {
            if (update.State == WorkSessionState.BreakDue)
            {
                _lastBreakDueNotification = update.Timestamp;
            }

            if (_systemNotifications?.IsAvailable == true)
            {
                _systemNotifications.Show(
                    notification.Title ?? "WinPet",
                    notification.Message ?? string.Empty);
            }
            else
            {
                Dispatcher.UIThread.Post(() => _manager.Show(notification));
            }
        }
    }
}
