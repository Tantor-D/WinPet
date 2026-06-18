using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinPet.Platform.Windows.Activity;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSessionStateTracker : IDisposable
{
    private int _isSessionLocked;
    private int _isSystemSuspended;
    private bool _subscribed;

    public WindowsSessionStateTracker()
    {
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _subscribed = true;
        }
        catch (InvalidOperationException)
        {
            // Non-interactive sessions may not expose SystemEvents.
            // Last-input detection remains usable in that environment.
        }
    }

    public bool IsSessionLocked => Volatile.Read(ref _isSessionLocked) == 1;

    public bool IsSystemSuspended => Volatile.Read(ref _isSystemSuspended) == 1;

    public void Dispose()
    {
        if (!_subscribed)
        {
            return;
        }

        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _subscribed = false;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        switch (args.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
                Volatile.Write(ref _isSessionLocked, 1);
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
                Volatile.Write(ref _isSessionLocked, 0);
                break;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        switch (args.Mode)
        {
            case PowerModes.Suspend:
                Volatile.Write(ref _isSystemSuspended, 1);
                break;

            case PowerModes.Resume:
                Volatile.Write(ref _isSystemSuspended, 0);
                break;
        }
    }
}
