using Voltray.Core;

namespace Voltray.Core.Tests;

public class BatteryAlertsTests
{
    static readonly AlertOptions Default = new(true, [20, 10], false, new HashSet<string>());

    static DeviceState Dev(int pct, ChargeState charge = ChargeState.Discharging, bool connected = true, string id = "m1") =>
        new(new DeviceInfo(id, "Mouse", null, DeviceCategory.Mouse, Confidence.Reported, "test"),
            new BatteryReading(pct, BatteryReading.LevelFromPercent(pct), charge, DateTimeOffset.Now, "test"), connected);

    /// Thresholds that fired, in order (0 for "charged").
    static List<int> Run(BatteryAlerts a, AlertOptions o, params DeviceState[] states) =>
        states.SelectMany(s => a.Evaluate([s], o)).Select(x => x.Threshold).ToList();

    [Fact]
    public void Warns_once_when_crossing_a_threshold() =>
        Assert.Equal([20], Run(new BatteryAlerts(), Default, Dev(25), Dev(20), Dev(19), Dev(18)));

    [Fact]
    public void Warns_at_startup_if_already_low() =>
        Assert.Equal([20], Run(new BatteryAlerts(), Default, Dev(15)));

    [Fact]
    public void Each_threshold_warns_in_turn() =>
        Assert.Equal([20, 10], Run(new BatteryAlerts(), Default, Dev(19), Dev(15), Dev(10), Dev(5)));

    [Fact]
    public void Crossing_several_at_once_sends_only_the_lowest() =>
        Assert.Equal([10], Run(new BatteryAlerts(), Default, Dev(30), Dev(8), Dev(7)));

    [Fact]
    public void Any_number_of_rules_in_any_order()
    {
        var o = Default with { Thresholds = [5, 30, 15] };
        Assert.Equal([30, 15, 5], Run(new BatteryAlerts(), o, Dev(40), Dev(30), Dev(15), Dev(4)));
    }

    [Fact]
    public void Small_wobble_above_threshold_does_not_rewarn() =>
        Assert.Equal([20], Run(new BatteryAlerts(), Default, Dev(20), Dev(22), Dev(20)));

    [Fact]
    public void Charging_rearms_the_warning() =>
        Assert.Equal([20, 20], Run(new BatteryAlerts(), Default, Dev(18), Dev(19, ChargeState.Charging), Dev(19)));

    [Fact]
    public void Rising_past_hysteresis_rearms_the_warning() =>
        Assert.Equal([20, 20], Run(new BatteryAlerts(), Default, Dev(18), Dev(25), Dev(20)));

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
        var a = new BatteryAlerts();
        var sent = new[] { Dev(90, ChargeState.Charging), Dev(100, ChargeState.Charging), Dev(100, ChargeState.Full) }
            .SelectMany(s => a.Evaluate([s], on)).Select(x => x.Kind).ToList();
        Assert.Equal([AlertKind.Charged], sent);
        Assert.Empty(new BatteryAlerts().Evaluate([Dev(100, ChargeState.Full)], Default));
    }

    [Fact]
    public void No_rules_means_no_low_warnings() =>
        Assert.Empty(Run(new BatteryAlerts(), Default with { Thresholds = [] }, Dev(3)));

    [Fact]
    public void Devices_are_tracked_independently() =>
        Assert.Equal(2, new BatteryAlerts().Evaluate([Dev(15, id: "a"), Dev(15, id: "b")], Default).Count);
}
