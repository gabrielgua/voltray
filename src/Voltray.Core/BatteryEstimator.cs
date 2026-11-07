namespace Voltray.Core;

/// <param name="Percent">Best guess of the current level. For devices that report in coarse steps
/// (e.g. 10 %) it is interpolated inside the reported range.</param>
/// <param name="Interpolated">True when <paramref name="Percent"/> is not what the device reported.</param>
/// <param name="Remaining">Estimated time until empty; null until there is enough history.</param>
public sealed record BatteryEstimate(int Percent, bool Interpolated, TimeSpan? Remaining, double? RatePerHour);

/// Turns a device's battery history into a drain speed, a remaining-time estimate and, for coarse
/// reporters, a smoother level. Pure functions over samples, so they are easy to test.
public static class BatteryEstimator
{
    /// Less time than this between the first and last level change of a session is too little to trust.
    static readonly TimeSpan MinSpan = TimeSpan.FromMinutes(20);
    /// Below this drain speed (≈ 2 weeks per charge) the estimate would be noise.
    const double MinRate = 0.3;

    /// The reporting granularity: 10 or 5 when every reading is a multiple of it, else 1.
    public static int StepSize(IEnumerable<BatterySample> samples)
    {
        var values = samples.Select(s => s.Percent).Where(p => p is > 0 and < 100).Distinct().ToList();
        if (values.Count < 2) return 1;
        if (values.All(v => v % 10 == 0)) return 10;
        if (values.All(v => v % 5 == 0)) return 5;
        return 1;
    }

    /// The current discharge: samples after the last charging sample.
    static List<BatterySample> Session(IReadOnlyList<BatterySample> history)
    {
        int start = 0;
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i].Charging) { start = i + 1; break; }
        return [.. history.Skip(start)];
    }

    /// Time the device was connected between two instants (it barely drains while off or away).
    static TimeSpan ActiveTime(IReadOnlyList<BatterySample> session, DateTimeOffset from, DateTimeOffset to)
    {
        var total = TimeSpan.Zero;
        for (int i = 0; i < session.Count; i++)
        {
            var segStart = session[i].Time;
            var segEnd = i + 1 < session.Count ? session[i + 1].Time : to;
            if (!session[i].Connected) continue;
            var a = segStart > from ? segStart : from;
            var b = segEnd < to ? segEnd : to;
            if (b > a) total += b - a;
        }
        return total;
    }

    /// Drain speed (% per connected hour) of the discharge in <paramref name="history"/>, measured
    /// between level changes only, so the quantisation of coarse reporters cancels out.
    public static double? SessionRate(IReadOnlyList<BatterySample> history)
    {
        var session = Session(history);
        var drops = new List<BatterySample>(); // first sample of each new, lower level
        for (int i = 1; i < session.Count; i++)
            if (session[i].Connected && session[i].Percent < session[i - 1].Percent) drops.Add(session[i]);
        if (drops.Count < 2) return null;
        var first = drops[0];
        var last = drops[^1];
        var active = ActiveTime(session, first.Time, last.Time);
        if (active < MinSpan) return null;
        double rate = (first.Percent - last.Percent) / active.TotalHours;
        return rate >= MinRate ? rate : null;
    }

    public static BatteryEstimate? Estimate(IReadOnlyList<BatterySample> history, DateTimeOffset now, double? learnedRate)
    {
        if (history.Count == 0) return null;
        var current = history[^1];
        if (current.Charging) return new BatteryEstimate(current.Percent, false, null, null);

        double? rate = SessionRate(history) ?? learnedRate;
        if (rate is not double r || r < MinRate) return new BatteryEstimate(current.Percent, false, null, null);

        // Coarse reporters show the bottom of a range ("40" = 40–49 %). When the device has just
        // entered a step, the real level is near the top of it; count down from there.
        int step = StepSize(history);
        double level = current.Percent;
        bool interpolated = false;
        if (step > 1 && current.Percent is > 0 and < 100)
        {
            var session = Session(history);
            int i = session.Count - 1;
            while (i > 0 && session[i - 1].Percent == current.Percent) i--; // first sample of the current step
            bool enteredByDropping = i > 0 && session[i - 1].Percent > current.Percent;
            if (enteredByDropping)
            {
                double drained = r * ActiveTime(session, session[i].Time, now).TotalHours;
                // Never leave the reported range: the next report corrects us anyway.
                level = Math.Clamp(current.Percent + step - 1 - drained, current.Percent, current.Percent + step - 1);
                interpolated = Math.Round(level) != current.Percent;
            }
        }

        var remaining = current.Connected ? TimeSpan.FromHours(level / r) : (TimeSpan?)null;
        return new BatteryEstimate((int)Math.Round(level), interpolated, remaining, r);
    }
}
