using Voltray.Core;

namespace Voltray.Core.Tests;

public class BatteryAlertsTests
{
    static readonly AlertOptions Default = new(true, 20, false, new HashSet<string>());

    static DeviceState Dev(int pct, ChargeState charge = ChargeState.Discharging, bool connected = true, string id = "m1") =>
        new(new DeviceInfo(id, "Mouse", null, DeviceCategory.Mouse, Confidence.Reported, "test"),
            new BatteryReading(pct, BatteryReading.LevelFromPercent(pct), charge, DateTimeOffset.Now, "test"), connected);

    static List<AlertKind> Run(BatteryAlerts a, AlertOptions o, params DeviceState[] states) =>
        states.SelectMany(s => a.Evaluate([s], o)).Select(x => x.Kind).ToList();

    [Fact]
    public void Warns_once_when_crossing_the_threshold() =>
        Assert.Equal([AlertKind.Low], Run(new BatteryAlerts(), Default, Dev(25), Dev(20), Dev(19), Dev(18)));

    [Fact]
    public void Warns_at_startup_if_already_low() =>
        Assert.Equal([AlertKind.Low], Run(new BatteryAlerts(), Default, Dev(15)));

    [Fact]
    public void Critical_follows_low() =>
        Assert.Equal([AlertKind.Low, AlertKind.Critical], Run(new BatteryAlerts(), Default, Dev(19), Dev(15), Dev(10), Dev(5)));

    [Fact]
    public void Jumping_straight_to_critical_sends_only_critical() =>
        Assert.Equal([AlertKind.Critical], Run(new BatteryAlerts(), Default, Dev(30), Dev(8), Dev(7)));

    [Fact]
    public void Small_wobble_above_threshold_does_not_rewarn() =>
        Assert.Equal([AlertKind.Low], Run(new BatteryAlerts(), Default, Dev(20), Dev(22), Dev(20)));

    [Fact]
    public void Charging_rearms_the_warning() =>
        Assert.Equal([AlertKind.Low, AlertKind.Low], Run(new BatteryAlerts(), Default, Dev(18), Dev(19, ChargeState.Charging), Dev(19)));

    [Fact]
    public void Rising_past_hysteresis_rearms_the_warning() =>
        Assert.Equal([AlertKind.Low, AlertKind.Low], Run(new BatteryAlerts(), Default, Dev(18), Dev(25), Dev(20)));

    [Fact]
    public void No_warnings_while_charging_or_disconnected() =>
        Assert.Empty(Run(new BatteryAlerts(), Default, Dev(5, ChargeState.Charging), Dev(5, connected: false)));

    [Fact]
    public void Muted_and_disabled_devices_stay_silent_but_do_not_warn_later_for_the_same_drop()
    {
        var a = new BatteryAlerts();
        var muted = Default with { Muted = new HashSet<string> { "m1" } };
        Assert.Empty(Run(a, muted, Dev(15)));
        Assert.Empty(Run(a, Default, Dev(14))); // unmuting does not replay the drop that already happened
        Assert.Empty(Run(new BatteryAlerts(), Default with { Enabled = false }, Dev(15)));
    }

    [Fact]
    public void Charged_notification_is_opt_in_and_sent_once()
    {
        var on = Default with { NotifyCharged = true };
        Assert.Equal([AlertKind.Charged], Run(new BatteryAlerts(), on, Dev(90, ChargeState.Charging), Dev(100, ChargeState.Charging), Dev(100, ChargeState.Full)));
        Assert.Empty(Run(new BatteryAlerts(), Default, Dev(100, ChargeState.Full)));
    }

    [Fact]
    public void Low_threshold_at_or_below_critical_has_no_separate_critical() =>
        Assert.Equal([AlertKind.Low], Run(new BatteryAlerts(), Default with { LowThreshold = 10 }, Dev(10), Dev(5)));

    [Fact]
    public void Devices_are_tracked_independently()
    {
        var a = new BatteryAlerts();
        var alerts = a.Evaluate([Dev(15, id: "a"), Dev(15, id: "b")], Default);
        Assert.Equal(2, alerts.Count);
    }
}
