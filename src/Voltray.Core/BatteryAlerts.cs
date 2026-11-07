namespace Voltray.Core;

public enum AlertKind { Low, Critical, Charged }

public sealed record Alert(string DeviceId, string DeviceName, DeviceCategory Category, AlertKind Kind, int Percent);

/// <param name="LowThreshold">Warn once when a discharging device drops to this level or below.</param>
/// <param name="NotifyCharged">Also tell when a charging device is full.</param>
/// <param name="Muted">Devices the user does not want notifications for.</param>
public sealed record AlertOptions(bool Enabled, int LowThreshold, bool NotifyCharged, IReadOnlySet<string> Muted)
{
    /// Second, last-chance warning. Only used when the low threshold is above it.
    public const int CriticalThreshold = 10;
    /// The level must climb this far above the threshold (or start charging) before warning again,
    /// so a reading that wobbles 20 → 21 → 20 does not notify twice.
    public const int Hysteresis = 5;
}

/// Decides when to notify. One warning per discharge cycle and level, never for disconnected devices.
public sealed class BatteryAlerts
{
    sealed class Armed { public bool Low = true, Critical = true, Charged = true; }

    readonly Dictionary<string, Armed> _devices = new();

    public List<Alert> Evaluate(IEnumerable<DeviceState> devices, AlertOptions options)
    {
        var alerts = new List<Alert>();
        foreach (var d in devices)
        {
            if (!d.Connected || d.Battery is not { Percent: int pct } b) continue;
            var armed = _devices.TryGetValue(d.Info.Id, out var a) ? a : _devices[d.Info.Id] = new Armed();
            bool charging = b.Charge is ChargeState.Charging or ChargeState.Full;
            bool wanted = options.Enabled && !options.Muted.Contains(d.Info.Id);
            Alert Make(AlertKind k) => new(d.Info.Id, d.Info.Name, d.Info.Category, k, pct);

            // Re-arm low-level warnings once the device is charging or clearly above the threshold.
            if (charging || pct >= options.LowThreshold + AlertOptions.Hysteresis) { armed.Low = true; armed.Critical = true; }
            if (!charging) armed.Charged = true;

            if (charging)
            {
                bool full = b.Charge == ChargeState.Full || pct >= 100;
                if (full && armed.Charged)
                {
                    armed.Charged = false;
                    if (wanted && options.NotifyCharged) alerts.Add(Make(AlertKind.Charged));
                }
                continue;
            }

            bool hasCritical = options.LowThreshold > AlertOptions.CriticalThreshold;
            if (hasCritical && pct <= AlertOptions.CriticalThreshold && armed.Critical)
            {
                armed.Critical = armed.Low = false; // critical supersedes low
                if (wanted) alerts.Add(Make(AlertKind.Critical));
            }
            else if (pct <= options.LowThreshold && armed.Low)
            {
                armed.Low = false;
                if (wanted) alerts.Add(Make(AlertKind.Low));
            }
        }
        return alerts;
    }
}
