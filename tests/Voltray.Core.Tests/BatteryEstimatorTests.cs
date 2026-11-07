using Voltray.Core;

namespace Voltray.Core.Tests;

public class BatteryEstimatorTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    static BatterySample S(double hours, int pct, bool charging = false, bool connected = true) =>
        new(T0.AddHours(hours), pct, charging, connected);

    [Fact]
    public void Rate_comes_from_level_changes_only()
    {
        // 80 → 70 in 2 h of connected time = 5 %/h
        var h = new[] { S(0, 90), S(0.5, 80), S(1.5, 75), S(2.5, 70) };
        Assert.Equal(5, BatteryEstimator.SessionRate(h)!.Value, 3);
    }

    [Fact]
    public void Disconnected_time_does_not_count_as_draining()
    {
        // drops 80 → 70 over 10 h of wall time, but 8 h of it disconnected → 2 h active = 5 %/h
        var h = new[] { S(0, 90), S(0.5, 80), S(1.5, 80, connected: false), S(9.5, 80), S(10.5, 70) };
        Assert.Equal(5, BatteryEstimator.SessionRate(h)!.Value, 3);
    }

    [Fact]
    public void Too_little_history_gives_no_rate() =>
        Assert.Null(BatteryEstimator.SessionRate([S(0, 90), S(0.1, 89), S(0.2, 88)]));

    [Fact]
    public void Only_the_current_discharge_counts()
    {
        var h = new[] { S(0, 50), S(1, 10), S(2, 10, charging: true), S(3, 100), S(4, 98), S(6, 94) };
        Assert.Equal(2, BatteryEstimator.SessionRate(h)!.Value, 3); // 98 → 94 in 2 h
    }

    [Fact]
    public void Remaining_time_is_level_over_rate()
    {
        var h = new[] { S(0, 91), S(1, 81), S(3, 71) }; // 5 %/h, fine-grained reporter
        var e = BatteryEstimator.Estimate(h, T0.AddHours(3), null)!;
        Assert.Equal(TimeSpan.FromHours(71 / 5.0), e.Remaining);
        Assert.False(e.Interpolated);
    }

    [Fact]
    public void Learned_rate_is_used_until_the_session_has_its_own()
    {
        var e = BatteryEstimator.Estimate([S(0, 60)], T0.AddMinutes(5), learnedRate: 3)!;
        Assert.Equal(TimeSpan.FromHours(20), e.Remaining);
    }

    [Fact]
    public void No_estimate_while_charging_or_without_any_rate()
    {
        Assert.Null(BatteryEstimator.Estimate([S(0, 60, charging: true)], T0, 3)!.Remaining);
        Assert.Null(BatteryEstimator.Estimate([S(0, 60)], T0, null)!.Remaining);
    }

    [Fact]
    public void Coarse_reporters_are_detected()
    {
        Assert.Equal(10, BatteryEstimator.StepSize([S(0, 100), S(1, 90), S(2, 80)]));
        Assert.Equal(5, BatteryEstimator.StepSize([S(0, 85), S(1, 80), S(2, 75)]));
        Assert.Equal(1, BatteryEstimator.StepSize([S(0, 66), S(1, 65)]));
    }

    [Fact]
    public void Coarse_level_counts_down_inside_the_reported_range()
    {
        // 10 %-step device draining 5 %/h; it just dropped into "40" (= 40–49 %).
        var h = new[] { S(0, 60), S(2, 50), S(4, 40) };
        Assert.Equal(49, BatteryEstimator.Estimate(h, T0.AddHours(4), null)!.Percent);
        var later = BatteryEstimator.Estimate(h, T0.AddHours(5), null)!; // 1 h later: 5 % lower
        Assert.Equal(44, later.Percent);
        Assert.True(later.Interpolated);
        Assert.Equal(40, BatteryEstimator.Estimate(h, T0.AddHours(9), null)!.Percent); // never below the report
    }

    [Fact]
    public void Fine_reporters_are_never_interpolated()
    {
        var h = new[] { S(0, 70), S(1, 65), S(2, 60) };
        var e = BatteryEstimator.Estimate(h, T0.AddHours(3), null)!;
        Assert.Equal(60, e.Percent);
        Assert.False(e.Interpolated);
    }

    [Fact]
    public void History_records_only_changes_and_learns_rates_on_charge()
    {
        var hist = new BatteryHistory();
        Assert.True(hist.Record("m", S(0, 90)));
        Assert.False(hist.Record("m", S(0.1, 90)));
        hist.Record("m", S(1, 80));
        hist.Record("m", S(3, 70));
        hist.Record("m", S(3.1, 70, charging: true));
        Assert.Equal(5, hist.LearnedRates["m"], 3);
    }

    [Fact]
    public void History_keeps_seven_days()
    {
        var hist = new BatteryHistory();
        hist.Record("m", S(0, 90));
        hist.Record("m", S(24 * 8, 80));
        hist.Prune(T0.AddDays(8));
        Assert.Single(hist.For("m"));
    }
}
