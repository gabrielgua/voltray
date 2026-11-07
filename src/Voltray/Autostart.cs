using Microsoft.Win32;
using Voltray.Core;

namespace Voltray;

/// "Start with Windows" via the per-user Run key (no admin, visible in Task Manager → Startup apps).
internal static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "Voltray";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            Log.Info($"autostart {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { Log.Error("autostart", e); }
    }

    /// Keeps the Run entry pointing at this exe if the user moved it.
    public static void Repair()
    {
        if (IsEnabled()) Set(true);
    }
}
