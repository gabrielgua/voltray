namespace Voltray.Core;

/// Last-resort category guess from the device name, used only when the provider could not
/// report a category (HID usage, Bluetooth class of device, HID++ device type...).
public static class DeviceClassifier
{
    // Order matters: "earbuds" must be tested before the generic "headphone" patterns.
    static readonly (DeviceCategory Category, string[] Patterns)[] NameRules =
    [
        (DeviceCategory.Earbuds, ["buds", "pods", "earbud", "wf-", "in-ear", "tws"]),
        (DeviceCategory.Headphones, ["headphone", "headset", "wh-", "arctis", "cloud ", "quietcomfort", "audio"]),
        (DeviceCategory.Mouse, ["mouse", "superlight", "viper", "deathadder", " mx master", "trackball"]),
        (DeviceCategory.Keyboard, ["keyboard", "keychron", " kb"]),
        (DeviceCategory.Controller, ["controller", "gamepad", "dualsense", "dualshock", "xbox"]),
    ];

    public static DeviceInfo Refine(DeviceInfo device)
    {
        if (device.CategoryConfidence >= Confidence.Reported) return device;
        var guess = FromName(device.Name);
        return guess is DeviceCategory c ? device with { Category = c, CategoryConfidence = Confidence.Guess } : device;
    }

    public static DeviceCategory? FromName(string name)
    {
        var n = " " + name.ToLowerInvariant() + " ";
        foreach (var (category, patterns) in NameRules)
            if (patterns.Any(n.Contains)) return category;
        return null;
    }
}
