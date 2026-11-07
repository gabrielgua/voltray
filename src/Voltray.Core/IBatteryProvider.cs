namespace Voltray.Core;

/// A source of devices and battery readings (Bluetooth, Logitech HID++, GameInput, ...).
/// Providers discover their own devices because only they know the topology
/// (e.g. six slots behind one LIGHTSPEED receiver).
public interface IBatteryProvider : IAsyncDisposable
{
    string Id { get; }

    /// Discover devices and start listening for events. Report everything through <paramref name="sink"/>.
    Task StartAsync(IDeviceSink sink, CancellationToken ct);

    /// Poll every known device once. Providers with push events may do little here.
    Task RefreshAsync(CancellationToken ct);

    /// Shortest polling interval this provider tolerates without hurting device power saving.
    TimeSpan MinimumPollInterval { get; }
}

/// Where providers publish what they find. Implemented by <see cref="DeviceRegistry"/>.
public interface IDeviceSink
{
    void Upsert(DeviceInfo device);
    void Report(string deviceId, BatteryReading reading);
    void Disconnected(string deviceId);
}
