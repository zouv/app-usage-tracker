using Microsoft.Win32;

namespace AppUsageTracker.Services;

public sealed class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AppUsageTracker";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true)
                        ?? Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                             ?? throw new InvalidOperationException("无法获取应用路径。");
            key.SetValue(ValueName, $"\"{executable}\" --background");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
