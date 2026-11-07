using System.Text;
using Voltray.Core;

namespace Voltray.Providers.Logitech;

/// HID++ 2.0 device type, as reported by feature 0x0005.
public enum HidppKind { Keyboard = 0, Remote = 1, Numpad = 2, Mouse = 3, Trackpad = 4, Trackball = 5, Presenter = 6, Receiver = 7, Headset = 8, Webcam = 9, Unknown = 255 }

/// A HID++ 2.0 device behind an endpoint (receiver slot 1..6, or 0xFF for direct connections).
public sealed class HidppDevice(HidppTransport transport, byte index)
{
    public const ushort FeatRoot = 0x0000, FeatDeviceInfo = 0x0003, FeatDeviceName = 0x0005,
        FeatBatteryStatus = 0x1000, FeatBatteryVoltage = 0x1001, FeatUnifiedBattery = 0x1004;

    readonly Dictionary<ushort, byte> _features = new();

    public HidppTransport Transport { get; } = transport;
    public byte Index { get; } = index;
    public double ProtocolVersion { get; private set; }
    public string? Name { get; private set; }
    public HidppKind Kind { get; private set; } = HidppKind.Unknown;
    /// Unit id from 0x0003; stable per physical device, unlike the receiver slot.
    public string? UnitId { get; private set; }

    /// Why the last <see cref="PingAsync"/> failed, e.g. "HID++ 1.0 error 0x09" (empty slot) or "timeout".
    public string? PingError { get; private set; }

    /// False when the slot is empty or the device is off. A sleeping wireless mouse takes ~0.9 s to answer.
    public async Task<bool> PingAsync(int timeoutMs = 3000)
    {
        try
        {
            var r = await Transport.RequestAsync(Index, 0x00, 0x1, new byte[] { 0, 0, 0x5A }, timeoutMs);
            ProtocolVersion = r[0] + r[1] / 10.0;
            return true;
        }
        catch (HidppException e) { PingError = e.Message; return false; }
        catch (TimeoutException) { PingError = "timeout"; return false; }
    }

    public async Task<byte?> FeatureIndexAsync(ushort featureId)
    {
        if (_features.TryGetValue(featureId, out var cached)) return cached == 0 ? null : cached;
        var r = await Transport.RequestAsync(Index, 0x00, 0x0, new byte[] { (byte)(featureId >> 8), (byte)featureId });
        _features[featureId] = r[0];
        return r[0] == 0 ? null : r[0];
    }

    public async Task LoadIdentityAsync()
    {
        if (await FeatureIndexAsync(FeatDeviceName) is byte f)
        {
            int len = (await Transport.RequestAsync(Index, f, 0x0))[0];
            var sb = new StringBuilder();
            while (sb.Length < len)
            {
                var chunk = await Transport.RequestAsync(Index, f, 0x1, new[] { (byte)sb.Length });
                sb.Append(Encoding.UTF8.GetString(chunk, 0, Math.Min(len - sb.Length, chunk.Length)));
            }
            Name = sb.ToString().TrimEnd('\0');
            try { Kind = (HidppKind)(await Transport.RequestAsync(Index, f, 0x2))[0]; } catch (HidppException) { }
        }
        if (await FeatureIndexAsync(FeatDeviceInfo) is byte di)
        {
            var r = await Transport.RequestAsync(Index, di, 0x0);
            if (r[1] != 0 || r[2] != 0 || r[3] != 0 || r[4] != 0) UnitId = Convert.ToHexString(r, 1, 4);
        }
    }

    /// Tries UNIFIED_BATTERY, then BATTERY_VOLTAGE, then BATTERY_STATUS. Null if the device has none.
    public async Task<BatteryReading?> ReadBatteryAsync()
    {
        var now = DateTimeOffset.Now;
        if (await FeatureIndexAsync(FeatUnifiedBattery) is byte ub)
        {
            var r = await Transport.RequestAsync(Index, ub, 0x1); // get_status
            // Level flags can disagree with state_of_charge (seen: "full" at 65%), so prefer the percentage.
            var level = (r[1] & 8) != 0 ? BatteryLevel.Full : (r[1] & 4) != 0 ? BatteryLevel.Good : (r[1] & 2) != 0 ? BatteryLevel.Low : (r[1] & 1) != 0 ? BatteryLevel.Critical : BatteryLevel.Unknown;
            int? pct = r[0] == 0 && r[1] != 0 ? null : r[0]; // 0% with flags set = device only reports levels
            return new BatteryReading(pct, pct is int p ? BatteryReading.LevelFromPercent(p) : level, ChargeFrom(r[2]), now, "HID++ 0x1004");
        }
        if (await FeatureIndexAsync(FeatBatteryVoltage) is byte bv)
        {
            var r = await Transport.RequestAsync(Index, bv, 0x0);
            int pct = VoltageCurve.Estimate(r[0] << 8 | r[1]);
            var charge = (r[2] & 0x80) == 0 ? ChargeState.Discharging : (r[2] & 0x07) switch { 0 => ChargeState.Charging, 1 => ChargeState.Full, _ => ChargeState.Unknown };
            return new BatteryReading(pct, BatteryReading.LevelFromPercent(pct), charge, now, "HID++ 0x1001 (estimated)");
        }
        if (await FeatureIndexAsync(FeatBatteryStatus) is byte bs)
        {
            var r = await Transport.RequestAsync(Index, bs, 0x0);
            return new BatteryReading(r[0], BatteryReading.LevelFromPercent(r[0]), ChargeFrom(r[2]), now, "HID++ 0x1000");
        }
        return null;
    }

    static ChargeState ChargeFrom(byte status) => status switch
    {
        0 => ChargeState.Discharging,
        1 or 2 => ChargeState.Charging,
        3 => ChargeState.Full,
        _ => ChargeState.Unknown,
    };
}
