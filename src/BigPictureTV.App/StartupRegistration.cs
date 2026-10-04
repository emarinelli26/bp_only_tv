using Microsoft.Win32;

namespace BigPictureTV.App;

/// <summary>"Start with Windows", through the per-user Run key (no admin rights needed).</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "BigPictureTV";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>If the app was moved, point the Run entry at where it is now.</summary>
    public static void RefreshPath(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is string current && current != $"\"{exePath}\"")
            key.SetValue(ValueName, $"\"{exePath}\"");
    }
}
