namespace Voltray.Core;

/// Tray icons are grouped by category, not by physical device.
public enum DeviceCategory { Mouse, Keyboard, Headphones, Earbuds, Controller, Other }

public enum ChargeState { Unknown, Discharging, Charging, Full }

/// Coarse level for devices that do not report an exact percentage.
public enum BatteryLevel { Unknown, Critical, Low, Good, Full }

/// How sure the classifier is about a category; a user override always wins.
public enum Confidence { None, Guess, Reported, User }

/// <param name="Percent">null when the device only reports a coarse <see cref="Level"/>.</param>
/// <param name="Source">Human-readable origin, e.g. "HID++ 0x1004" or "Bluetooth HFP".</param>
public sealed record BatteryReading(int? Percent, BatteryLevel Level, ChargeState Charge, DateTimeOffset Timestamp, string Source)
{
    public static BatteryLevel LevelFromPercent(int percent) => percent switch
    {
        <= 10 => BatteryLevel.Critical,
        <= 20 => BatteryLevel.Low,
        < 95 => BatteryLevel.Good,
        _ => BatteryLevel.Full,
    };
}

/// <param name="Id">Stable across restarts and reconnects (provider-scoped, e.g. "logitech:4093:SERIAL").</param>
public sealed record DeviceInfo(
    string Id,
    string Name,
    string? Manufacturer,
    DeviceCategory Category,
    Confidence CategoryConfidence,
    string ProviderId);

public sealed record DeviceState(DeviceInfo Info, BatteryReading? Battery, bool Connected);
