using Voltray.Core;

namespace Voltray.Core.Tests;

public class DeviceRegistryTests
{
    static DeviceInfo Mouse(string id = "m1") => new(id, "Mouse", null, DeviceCategory.Mouse, Confidence.Reported, "test");
    static BatteryReading Reading(int pct) => new(pct, BatteryReading.LevelFromPercent(pct), ChargeState.Discharging, DateTimeOffset.Now, "test");

    [Fact]
    public void Keeps_last_reading_when_device_disconnects()
    {
        var r = new DeviceRegistry();
        r.Upsert(Mouse());
        r.Report("m1", Reading(62));
        r.Disconnected("m1");

        var s = Assert.Single(r.Snapshot());
        Assert.False(s.Connected);
        Assert.Equal(62, s.Battery?.Percent);
    }

    [Fact]
    public void User_category_override_survives_provider_reupsert()
    {
        var r = new DeviceRegistry();
        r.OverrideCategory("m1", DeviceCategory.Other);
        r.Upsert(Mouse());

        var s = Assert.Single(r.Snapshot());
        Assert.Equal(DeviceCategory.Other, s.Info.Category);
        Assert.Equal(Confidence.User, s.Info.CategoryConfidence);
    }

    [Fact]
    public void Clearing_an_override_restores_the_reported_category()
    {
        var r = new DeviceRegistry();
        r.Upsert(Mouse());
        r.OverrideCategory("m1", DeviceCategory.Keyboard);
        Assert.Equal(DeviceCategory.Keyboard, r.Snapshot()[0].Info.Category);

        r.OverrideCategory("m1", null);
        var info = r.Snapshot()[0].Info;
        Assert.Equal(DeviceCategory.Mouse, info.Category);
        Assert.Equal(Confidence.Reported, info.CategoryConfidence);
    }

    [Fact]
    public void Readings_for_unknown_devices_are_ignored()
    {
        var r = new DeviceRegistry();
        r.Report("ghost", Reading(10));
        Assert.Empty(r.Snapshot());
    }

    [Fact]
    public void Changed_fires_on_every_update()
    {
        var r = new DeviceRegistry();
        int n = 0;
        r.Changed += () => n++;
        r.Upsert(Mouse());
        r.Report("m1", Reading(50));
        r.Disconnected("m1");
        Assert.Equal(3, n);
    }
}
