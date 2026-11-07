using System.Text.Json;
using Voltray.Core;

namespace Voltray.Ipc;

/// Tray icon styles. Every style shows the category symbol, so icons can be told apart.
/// Stored by name in settings.json; retired styles stay listed so old files still load (see Settings.Load).
public enum IconStyle
{
    /// Symbol with a rounded level bar below.
    Glyph = 1,
    /// Symbol with a rounded vertical level bar at its side.
    GlyphBarSide = 6,
    /// Symbol tinted by the battery level in steps (green, yellow, orange, red).
    GlyphColorSteps = 9,
    /// Symbol on top, percentage below in a pixel font.
    GlyphNumber = 10,

    // Retired during design reviews
    Percentage = 0,
    GlyphPercentage = 2,
    GlyphBadge = 3,
    GlyphPercentageSide = 4,
    GlyphBadgeSide = 5,
    GlyphNumberCorner = 7,
    GlyphColor = 8,
}
/// One tray icon per device category (devices of a category share it), or one per device.
public enum TrayMode { PerCategory, PerDevice }

/// What a disconnected device looks like in the tray (it always stays listed in the window).
public enum DisconnectedInTray { Dim, Hide }

/// Notify once when a discharging device drops to <see cref="Percent"/> or below.
public sealed class AlertRule
{
    public int Percent { get; set; }
    /// Shown under the title; null or empty = default text.
    public string? Message { get; set; }
}

/// User preferences, stored as JSON in %AppData%\Voltray\settings.json.
/// Only the tray host writes this file; the settings window changes it through IPC.
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public IconStyle IconStyle { get; set; } = IconStyle.Glyph;
    /// Show "%" after the number in styles that have one.
    public bool ShowPercentSign { get; set; } = true;
    /// Tint the number with the level colour (green, yellow, orange, red) instead of the taskbar text colour.
    public bool ColorNumber { get; set; }
    public int RefreshMinutes { get; set; } = 5;
    public HashSet<DeviceCategory> HiddenCategories { get; set; } = [];
    /// Devices the user removed from the tray (still listed in the settings window).
    public HashSet<string> HiddenDevices { get; set; } = [];
    public Dictionary<string, DeviceCategory> CategoryOverrides { get; set; } = [];
    /// Mirrors the HKCU Run entry; the registry is the source of truth.
    public bool StartWithWindows { get; set; }

    public TrayMode TrayMode { get; set; } = TrayMode.PerCategory;
    public DisconnectedInTray Disconnected { get; set; } = DisconnectedInTray.Dim;
    /// Icon picked per device (key into Voltray.Core.Icons.DeviceIcons); missing = category icon.
    public Dictionary<string, string> DeviceIcons { get; set; } = [];

    public bool NotifyLowBattery { get; set; } = true;
    public List<AlertRule> AlertRules { get; set; } = DefaultAlertRules();
    public bool NotifyCharged { get; set; }
    /// Devices the user does not want notifications for.
    public HashSet<string> MutedDevices { get; set; } = [];

    /// Single threshold from before alert rules existed; read once and migrated.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? LowBatteryThreshold { get; set; }

    public static List<AlertRule> DefaultAlertRules() =>
    [
        new() { Percent = 20, Message = "Bateria baixa, carregue em breve" },
        new() { Percent = 10, Message = "Bateria crítica" },
    ];

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltray", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize(File.ReadAllText(FilePath), IpcJson.Default.Settings) is { } s)
            {
                s.IconStyle = s.IconStyle switch
                {
                    IconStyle.Glyph or IconStyle.GlyphBarSide or IconStyle.GlyphColorSteps or IconStyle.GlyphNumber => s.IconStyle,
                    IconStyle.GlyphColor => IconStyle.GlyphColorSteps,
                    IconStyle.Percentage or IconStyle.GlyphPercentage or IconStyle.GlyphPercentageSide
                        or IconStyle.GlyphBadge or IconStyle.GlyphBadgeSide or IconStyle.GlyphNumberCorner => IconStyle.GlyphNumber,
                    _ => IconStyle.Glyph,
                };
                if (s.LowBatteryThreshold is int t) // single threshold → rules (plus the old fixed 10 % warning)
                {
                    s.AlertRules = t > 10 ? [new() { Percent = t, Message = "Bateria baixa, carregue em breve" }, new() { Percent = 10, Message = "Bateria crítica" }]
                        : [new() { Percent = t, Message = "Bateria crítica" }];
                    s.LowBatteryThreshold = null;
                }
                return s;
            }
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
