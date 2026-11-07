namespace Voltray.Core;

public enum AlertKind { Low, Charged }

/// <param name="Threshold">The rule that fired (Low only).</param>
public sealed record Alert(string DeviceId, string DeviceName, DeviceCategory Category, AlertKind Kind, int Percent, int Threshold = 0);

/// <param name="Thresholds">Warn once when a discharging device drops to each of these levels or below.</param>
/// <param name="NotifyCharged">Also tell when a charging device is full.</param>
/// <param name="Muted">Devices the user does not want notifications for.</param>
public sealed record AlertOptions(bool Enabled, IReadOnlyList<int> Thresholds, bool NotifyCharged, IReadOnlySet<string> Muted)
{
    /// The level must climb this far above a threshold (or start charging) before it warns again,
    /// so a reading that wobbles 20 → 21 → 20 does not notify twice.
    public const int Hysteresis = 5;
}

/// Decides when to notify. Each threshold warns once per discharge; when several are crossed at once,
/// only the lowest (most urgent) one is shown. Never for disconnected devices.
public sealed class BatteryAlerts
{
    sealed class DeviceAlerts
    {
        public readonly HashSet<int> Fired = [];
        public bool ChargedArmed = true;
    }

    readonly Dictionary<string, DeviceAlerts> _devices = new();

    public List<Alert> Evaluate(IEnumerable<DeviceState> devices, AlertOptions options)
    {
        var alerts = new List<Alert>();
        foreach (var d in devices)
        {
            if (!d.Connected || d.Battery is not { Percent: int pct } b) continue;
            var state = _devices.TryGetValue(d.Info.Id, out var s) ? s : _devices[d.Info.Id] = new DeviceAlerts();
            bool charging = b.Charge is ChargeState.Charging or ChargeState.Full;
            bool wanted = options.Enabled && !options.Muted.Contains(d.Info.Id);
            Alert Make(AlertKind k, int threshold = 0) => new(d.Info.Id, d.Info.Name, d.Info.Category, k, pct, threshold);

            // Re-arm thresholds once the device is charging or clearly above them.
            state.Fired.RemoveWhere(t => charging || pct >= t + AlertOptions.Hysteresis);
            if (!charging) state.ChargedArmed = true;

            if (charging)
            {
                bool full = b.Charge == ChargeState.Full || pct >= 100;
                if (full && state.ChargedArmed)
                {
                    state.ChargedArmed = false;
                    if (wanted && options.NotifyCharged) alerts.Add(Make(AlertKind.Charged));
                }
                continue;
            }

            var crossed = options.Thresholds.Where(t => pct <= t && !state.Fired.Contains(t)).ToList();
            if (crossed.Count == 0) continue;
            foreach (var t in crossed) state.Fired.Add(t); // muted or not: a drop is never replayed later
            if (wanted) alerts.Add(Make(AlertKind.Low, crossed.Min()));
        }
        return alerts;
    }
}
