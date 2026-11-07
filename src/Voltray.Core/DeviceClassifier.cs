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

    /// Bluetooth Classic Class of Device, already split into the 5-bit major and 6-bit minor class.
    public static DeviceCategory? FromClassOfDevice(int major, int minor) => major switch
    {
        0x04 => minor switch // Audio/Video
        {
            0x01 or 0x02 or 0x06 => DeviceCategory.Headphones, // wearable headset, hands-free, headphones
            _ => null,                                          // speakers, car audio...
        },
        0x05 => (minor >> 4) switch // Peripheral: bits 4-5 keyboard/pointing, bits 0-3 sub-type
        {
            0b01 => DeviceCategory.Keyboard,
            0b10 => DeviceCategory.Mouse,
            0b11 => DeviceCategory.Keyboard, // combo keyboard + pointing device
            _ => (minor & 0x0F) is 0x01 or 0x02 ? DeviceCategory.Controller : null, // joystick, gamepad
        },
        _ => null,
    };

    /// Bluetooth LE GAP Appearance value (Bluetooth Assigned Numbers, section 2.6).
    public static DeviceCategory? FromBleAppearance(int appearance) => appearance switch
    {
        0x03C1 => DeviceCategory.Keyboard,
        0x03C2 => DeviceCategory.Mouse,
        0x03C3 or 0x03C4 => DeviceCategory.Controller, // joystick, gamepad
        0x0941 => DeviceCategory.Earbuds,
        0x0942 or 0x0943 or 0x0944 => DeviceCategory.Headphones, // headset, headphones, neckband
        _ when appearance >> 6 == 0x25 => DeviceCategory.Headphones, // other wearable audio
        _ => null,
    };

    public static DeviceCategory? FromName(string name)
    {
        var n = " " + name.ToLowerInvariant() + " ";
        foreach (var (category, patterns) in NameRules)
            if (patterns.Any(n.Contains)) return category;
        return null;
    }
}
