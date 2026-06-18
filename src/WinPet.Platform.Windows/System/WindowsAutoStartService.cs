using System.Runtime.Versioning;
using Microsoft.Win32;
using WinPet.Core.Platform;

namespace WinPet.Platform.Windows.Startup;

[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStartService : IAutoStartService
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinPet";
    private readonly string _executablePath;

    public WindowsAutoStartService(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(ValueName) as string;
        return string.Equals(
            value,
            Quote(_executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Quote(_executablePath));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
