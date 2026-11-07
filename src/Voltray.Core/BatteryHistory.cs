namespace Voltray.Core;

/// One point of a device's battery history. Recorded only when something changes
/// (level, charging, connection), so a week of history stays small.
public sealed record BatterySample(DateTimeOffset Time, int Percent, bool Charging, bool Connected);

/// Per-device battery history (last 7 days) plus a learned drain rate that survives restarts.
/// Not thread-safe: the host only touches it on its UI thread.
public sealed class BatteryHistory
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    public Dictionary<string, List<BatterySample>> Samples { get; set; } = [];
    /// Typical discharge speed per device, in % per hour (exponential moving average over sessions).
    public Dictionary<string, double> LearnedRates { get; set; } = [];

    /// Appends a sample if it differs from the last one. Returns true when something was recorded.
    public bool Record(string deviceId, BatterySample sample)
    {
        if (!Samples.TryGetValue(deviceId, out var list)) Samples[deviceId] = list = [];
        if (list.Count > 0 && list[^1] is var last && last.Percent == sample.Percent && last.Charging == sample.Charging && last.Connected == sample.Connected)
            return false;
        list.Add(sample);
        // A finished discharge (charging started) teaches the device's typical drain speed.
        if (sample.Charging && BatteryEstimator.SessionRate(list.Take(list.Count - 1).ToList()) is double rate)
            LearnedRates[deviceId] = LearnedRates.TryGetValue(deviceId, out var old) ? old * 0.6 + rate * 0.4 : rate;
        return true;
    }

    public IReadOnlyList<BatterySample> For(string deviceId) =>
        Samples.TryGetValue(deviceId, out var list) ? list : [];

    public void Prune(DateTimeOffset now)
    {
        foreach (var list in Samples.Values) list.RemoveAll(s => now - s.Time > Keep);
        foreach (var id in Samples.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) Samples.Remove(id);
    }
}
