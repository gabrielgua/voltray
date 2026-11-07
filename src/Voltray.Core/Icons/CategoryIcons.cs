namespace Voltray.Core.Icons;

/// The default icon of each device category (a key into <see cref="DeviceIcons"/>).
public static class CategoryIcons
{
    public const double Stroke = 1.5;

    public static string Key(DeviceCategory c) => c switch
    {
        DeviceCategory.Mouse => "mouse-01",
        DeviceCategory.Keyboard => "keyboard",
        DeviceCategory.Headphones => "headphones",
        DeviceCategory.Earbuds => "airpod-03",
        DeviceCategory.Controller => "gamepad-01",
        _ => "monitor-speaker",
    };

    /// SVG path of the category's default icon.
    public static string For(DeviceCategory c) => DeviceIcons.Find(Key(c))!.Path;

    /// SVG path for a device: the icon the user picked for it, else its category's.
    public static string For(DeviceCategory c, string? iconKey) => DeviceIcons.Find(iconKey)?.Path ?? For(c);
}