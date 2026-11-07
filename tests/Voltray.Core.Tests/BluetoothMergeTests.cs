using Voltray.Core;
using Voltray.Providers.Bluetooth;

namespace Voltray.Core.Tests;

public class BluetoothMergeTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Now;

    static Endpoint Classic(string name, bool connected = true, int? major = 0x04, int? minor = 0x01) =>
        new("Bluetooth#Bluetooth00:11:22:33:44:55-aa:bb:cc:dd:ee:ff", name, false, connected, major, minor, null);

    static Endpoint Le(string name, bool connected = true, int? appearance = null) =>
        new("BluetoothLE#BluetoothLE00:11:22:33:44:55-aa:bb:cc:dd:ee:ff", name, true, connected, null, null, appearance);

    static MergedDevice? Merge(IReadOnlyList<Endpoint> endpoints, int?[] nodes, int? gatt = null, bool listed = false) =>
        BluetoothMerge.Merge("bt:1", "bluetooth", endpoints, nodes, gatt, listed, Now);

    [Fact]
    public void Headset_with_windows_level_is_listed_as_headphones()
    {
        var m = Merge([Classic("WH-1000XM5")], [70])!;
        Assert.Equal(DeviceCategory.Headphones, m.Info.Category);
        Assert.Equal(Confidence.Reported, m.Info.CategoryConfidence);
        Assert.Equal(70, m.Reading?.Percent);
        Assert.Equal(BluetoothMerge.WindowsSource, m.Reading?.Source);
        Assert.True(m.Connected);
    }

    [Fact]
    public void Devices_that_never_reported_a_battery_are_not_listed()
    {
        Assert.Null(Merge([Classic("JBL Speaker", major: 0x04, minor: 0x05)], [null]));
    }

    [Fact]
    public void A_listed_device_stays_listed_when_it_reports_nothing()
    {
        var m = Merge([Classic("WH-1000XM5", connected: false)], [], listed: true)!;
        Assert.False(m.Connected);
        Assert.Null(m.Reading);
    }

    [Fact]
    public void Dual_mode_device_prefers_the_classic_name_and_windows_level()
    {
        var m = Merge([Le("XM5"), Classic("WH-1000XM5")], [65], gatt: 60)!;
        Assert.Equal("WH-1000XM5", m.Info.Name);
        Assert.Equal(65, m.Reading?.Percent);
    }

    [Fact]
    public void Gatt_level_is_used_when_windows_has_none()
    {
        var m = Merge([Le("MX Keys", appearance: 0x03C1)], [null], gatt: 88)!;
        Assert.Equal(DeviceCategory.Keyboard, m.Info.Category);
        Assert.Equal(88, m.Reading?.Percent);
        Assert.Equal(BluetoothMerge.GattSource, m.Reading?.Source);
    }

    [Fact]
    public void Highest_node_level_wins_and_out_of_range_values_are_ignored()
    {
        var m = Merge([Classic("Buds")], [255, 40, null, 55])!;
        Assert.Equal(55, m.Reading?.Percent);
    }

    [Fact]
    public void Headset_class_with_earbud_name_becomes_earbuds()
    {
        var m = Merge([Classic("Galaxy Buds2 Pro")], [80])!;
        Assert.Equal(DeviceCategory.Earbuds, m.Info.Category);
    }

    [Fact]
    public void Unknown_class_falls_back_to_other_with_no_confidence()
    {
        var m = Merge([Classic("Thing", major: null, minor: null)], [50])!;
        Assert.Equal(DeviceCategory.Other, m.Info.Category);
        Assert.Equal(Confidence.None, m.Info.CategoryConfidence); // lets the name classifier try later
    }

    [Fact]
    public void Connected_if_any_endpoint_is_connected()
    {
        var m = Merge([Classic("WH-1000XM5", connected: false), Le("XM5", connected: true)], [70])!;
        Assert.True(m.Connected);
    }
}
