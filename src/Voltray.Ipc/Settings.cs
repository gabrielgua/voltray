using System.Text.Json;
using Voltray.Core;

namespace Voltray.Ipc;

public enum IconStyle { Percentage, Glyph }

/// User preferences, stored as JSON in %AppData%\Voltray\settings.json.
/// Only the tray host writes this file; the settings window changes it through IPC.
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public IconStyle IconStyle { get; set; } = IconStyle.Percentage;
    public int RefreshMinutes { get; set; } = 5;
    public HashSet<DeviceCategory> HiddenCategories { get; set; } = [];
    /// Devices the user removed from the tray (still listed in the settings window).
    public HashSet<string> HiddenDevices { get; set; } = [];
    public Dictionary<string, DeviceCategory> CategoryOverrides { get; set; } = [];
    /// Mirrors the HKCU Run entry; the registry is the source of truth.
    public bool StartWithWindows { get; set; }

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltray", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), IpcJson.Default.Settings) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { } // corrupt or locked: fall back to defaults
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, IpcJson.Default.Settings));
    }

    public Settings Clone() => JsonSerializer.Deserialize(JsonSerializer.Serialize(this, IpcJson.Default.Settings), IpcJson.Default.Settings)!;
}
