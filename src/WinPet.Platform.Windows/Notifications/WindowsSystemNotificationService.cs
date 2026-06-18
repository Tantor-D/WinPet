using System.Runtime.Versioning;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WinPet.Core.Platform;

namespace WinPet.Platform.Windows.Notifications;

[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsSystemNotificationService :
    ISystemNotificationService
{
    private bool _registered;

    public WindowsSystemNotificationService()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  System.Runtime.InteropServices.COMException)
        {
            _registered = false;
        }
    }

    public bool IsAvailable => _registered;

    public void Show(string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    public void Dispose()
    {
        if (!_registered)
        {
            return;
        }

        AppNotificationManager.Default.Unregister();
        _registered = false;
    }
}
