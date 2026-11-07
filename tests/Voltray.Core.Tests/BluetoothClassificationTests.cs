using Voltray.Core;

namespace Voltray.Core.Tests;

public class BluetoothClassificationTests
{
    [Theory]
    [InlineData(0x04, 0x01, DeviceCategory.Headphones)] // wearable headset
    [InlineData(0x04, 0x02, DeviceCategory.Headphones)] // hands-free
    [InlineData(0x04, 0x06, DeviceCategory.Headphones)] // headphones
    [InlineData(0x05, 0x10, DeviceCategory.Keyboard)]
    [InlineData(0x05, 0x20, DeviceCategory.Mouse)]
    [InlineData(0x05, 0x30, DeviceCategory.Keyboard)]   // combo
    [InlineData(0x05, 0x02, DeviceCategory.Controller)] // gamepad
    [InlineData(0x05, 0x01, DeviceCategory.Controller)] // joystick
    public void Maps_class_of_device(int major, int minor, DeviceCategory expected) =>
        Assert.Equal(expected, DeviceClassifier.FromClassOfDevice(major, minor));

    [Theory]
    [InlineData(0x04, 0x05)] // loudspeaker
    [InlineData(0x02, 0x03)] // phone
    [InlineData(0x05, 0x00)] // uncategorized peripheral
    public void Leaves_unrelated_class_of_device_unclassified(int major, int minor) =>
        Assert.Null(DeviceClassifier.FromClassOfDevice(major, minor));

    [Theory]
    [InlineData(0x03C1, DeviceCategory.Keyboard)]
    [InlineData(0x03C2, DeviceCategory.Mouse)]
    [InlineData(0x03C4, DeviceCategory.Controller)]
    [InlineData(0x0941, DeviceCategory.Earbuds)]
    [InlineData(0x0943, DeviceCategory.Headphones)]
    [InlineData(0x0940, DeviceCategory.Headphones)] // generic wearable audio
    public void Maps_ble_appearance(int appearance, DeviceCategory expected) =>
        Assert.Equal(expected, DeviceClassifier.FromBleAppearance(appearance));

    [Fact]
    public void Generic_hid_appearance_is_unclassified() => Assert.Null(DeviceClassifier.FromBleAppearance(0x03C0));
}
