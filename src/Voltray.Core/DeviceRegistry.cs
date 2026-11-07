namespace Voltray.Core;

/// Thread-safe store of every device any provider has reported. Providers write from
/// background threads; the tray and UI read snapshots and react to <see cref="Changed"/>.
public sealed class DeviceRegistry : IDeviceSink
{
    readonly Lock _lock = new();
    readonly Dictionary<string, DeviceState> _devices = new();
    readonly Dictionary<string, DeviceInfo> _reported = new(); // as the provider (+ classifier) saw it, before overrides
    readonly Dictionary<string, DeviceCategory> _categoryOverrides = new();

    /// Raised after any change, on the thread that made it.
    public event Action? Changed;

    public IReadOnlyList<DeviceState> Snapshot()
    {
        lock (_lock) return [.. _devices.Values];
    }

    /// User-chosen category, or null to go back to automatic. Survives provider re-reports.
    public void OverrideCategory(string deviceId, DeviceCategory? category)
    {
        lock (_lock)
        {
            if (category is DeviceCategory c) _categoryOverrides[deviceId] = c;
            else _categoryOverrides.Remove(deviceId);
            if (_devices.TryGetValue(deviceId, out var s) && _reported.TryGetValue(deviceId, out var reported))
                _devices[deviceId] = s with { Info = Effective(reported) };
        }
        Changed?.Invoke();
    }

    DeviceInfo Effective(DeviceInfo reported) =>
        _categoryOverrides.TryGetValue(reported.Id, out var c) ? reported with { Category = c, CategoryConfidence = Confidence.User } : reported;

    public void Upsert(DeviceInfo device)
    {
        device = DeviceClassifier.Refine(device);
        lock (_lock)
        {
            _reported[device.Id] = device;
            var info = Effective(device);
            _devices[device.Id] = _devices.TryGetValue(device.Id, out var s)
                ? s with { Info = info, Connected = true }
                : new DeviceState(info, null, true);
        }
        Changed?.Invoke();
    }

    public void Report(string deviceId, BatteryReading reading)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var s)) return;
            _devices[deviceId] = s with { Battery = reading, Connected = true };
        }
        Changed?.Invoke();
    }

    /// Keeps the last reading so the UI can show "disconnected, last seen 62%".
    public void Disconnected(string deviceId)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(deviceId, out var s) || !s.Connected) return;
            _devices[deviceId] = s with { Connected = false };
        }
        Changed?.Invoke();
    }
}
