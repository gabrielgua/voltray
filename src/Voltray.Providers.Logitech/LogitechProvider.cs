using System.Collections.Concurrent;
using System.Diagnostics;
using Voltray.Core;

namespace Voltray.Providers.Logitech;

/// Logitech devices over HID++ 2.0: LIGHTSPEED / Unifying / Bolt receivers, USB cable and
/// Bluetooth. Does not need G HUB and coexists with it.
public sealed class LogitechProvider : IBatteryProvider
{
    static readonly byte[] Slots = [1, 2, 3, 4, 5, 6, 0xFF];

    readonly List<HidppTransport> _transports = [];
    readonly ConcurrentDictionary<(HidppTransport, byte), (HidppDevice Device, string Id)> _devices = new();
    IDeviceSink? _sink;

    public string Id => "logitech";

    /// Devices push battery changes, so polling is only a safety net.
    public TimeSpan MinimumPollInterval => TimeSpan.FromMinutes(1);

    public async Task StartAsync(IDeviceSink sink, CancellationToken ct)
    {
        _sink = sink;
        var endpoints = HidppTransport.Discover();
        Log.Info($"logitech: {endpoints.Count} HID++ endpoint(s): {string.Join(", ", endpoints.Select(e => $"{e.Pid:X4}{(e.ShortPath is null ? " (no short)" : "")}"))}");
        foreach (var ep in endpoints)
        {
            HidppTransport t;
            try { t = new HidppTransport(ep); }
            catch (IOException e) { Log.Error($"cannot open {ep.LongPath}", e); continue; }
            _transports.Add(t);
            t.Notification += r => _ = OnNotification(t, r);
            foreach (var slot in Slots) await ProbeAsync(t, slot);
        }
    }

    async Task ProbeAsync(HidppTransport t, byte slot)
    {
        var d = new HidppDevice(t, slot);
        if (!await d.PingAsync())
        {
            // HID++ 1.0 error 0x08 (unknown device) = nothing paired in this slot. Anything else
            // (0x09 resource error = paired but off, timeout = asleep) is worth knowing.
            if (d.PingError is { } err && !err.EndsWith("0x08") && slot != 0xFF) Log.Info($"logitech: slot {slot} not reachable ({err}), will retry");
            return;
        }
        if (d.ProtocolVersion < 2) return; // the receiver itself answers HID++ 1.0 on 0xFF
        try
        {
            await d.LoadIdentityAsync();
            string id = $"logitech:{d.UnitId ?? $"{t.Endpoint.Pid:X4}-{slot:X2}"}";
            _devices[(t, slot)] = (d, id);
            Log.Info($"logitech: slot {slot} = {d.Name} ({d.Kind}, HID++ {d.ProtocolVersion:0.0}) id={id}");
            _sink!.Upsert(new DeviceInfo(id, d.Name ?? "Logitech device", "Logitech", CategoryOf(d.Kind),
                d.Kind == HidppKind.Unknown ? Confidence.None : Confidence.Reported, Id));
            await ReadAsync(d, id);
        }
        catch (Exception e) when (e is HidppException or TimeoutException) { Log.Error($"logitech: slot {slot} probe failed", e); }
    }

    async Task ReadAsync(HidppDevice d, string id)
    {
        try
        {
            if (await d.ReadBatteryAsync() is BatteryReading b) _sink!.Report(id, b);
        }
        catch (Exception e) when (e is HidppException or TimeoutException) { _sink!.Disconnected(id); }
    }

    async Task OnNotification(HidppTransport t, byte[] r)
    {
        byte slot = r[1];
        // HID++ 1.0 "device connection" (0x41): bit 6 of byte 4 set = link lost
        if (r[0] == HidppTransport.ReportShort && r[2] == 0x41)
        {
            bool lost = (r[4] & 0x40) != 0;
            if (_devices.TryGetValue((t, slot), out var known))
            {
                if (lost) _sink!.Disconnected(known.Id);
                else await ReadAsync(known.Device, known.Id);
            }
            else if (!lost) await ProbeAsync(t, slot);
            return;
        }
        if (_devices.TryGetValue((t, slot), out var dev)) await ReadAsync(dev.Device, dev.Id); // battery event
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        foreach (var (d, id) in _devices.Values) await ReadAsync(d, id);
        // Devices that were asleep or off at startup: empty slots answer instantly, so this is cheap.
        foreach (var t in _transports)
            foreach (var slot in Slots)
                if (!_devices.ContainsKey((t, slot))) await ProbeAsync(t, slot);
    }

    static DeviceCategory CategoryOf(HidppKind kind) => kind switch
    {
        HidppKind.Mouse or HidppKind.Trackball or HidppKind.Trackpad => DeviceCategory.Mouse,
        HidppKind.Keyboard or HidppKind.Numpad => DeviceCategory.Keyboard,
        HidppKind.Headset => DeviceCategory.Headphones,
        _ => DeviceCategory.Other,
    };

    public ValueTask DisposeAsync()
    {
        foreach (var t in _transports) t.Dispose();
        return ValueTask.CompletedTask;
    }
}
