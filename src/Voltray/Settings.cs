using System.Text.Json;
using System.Text.Json.Serialization;
using Voltray.Core;
using Voltray.Tray;

namespace Voltray;

/// User preferences, stored as JSON in %AppData%\Voltray\settings.json.
public sealed class Settings
{
    public int Version { get; set; } = 1;
    public IconStyle IconStyle { get; set; } = IconStyle.Percentage;
    public int RefreshMinutes { get; set; } = 5;
    public HashSet<DeviceCategory> HiddenCategories { get; set; } = [];
    public Dictionary<string, DeviceCategory> CategoryOverrides { get; set; } = [];

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Voltray", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.Settings) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { } // corrupt or locked: fall back to defaults
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
