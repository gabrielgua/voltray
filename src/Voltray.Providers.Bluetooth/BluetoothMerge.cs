using Voltray.Core;

namespace Voltray.Providers.Bluetooth;

/// A paired Bluetooth endpoint. Dual-mode devices have a Classic and an LE endpoint in one container.
internal sealed record Endpoint(string Id, string Name, bool IsLe, bool Connected, int? CodMajor, int? CodMinor, int? Appearance);

/// What one physical device (device container) looks like after merging its endpoints and battery sources.
internal sealed record MergedDevice(DeviceInfo Info, bool Connected, BatteryReading? Reading);

/// Pure merge logic, kept apart from WinRT so it can be unit-tested without hardware.
internal static class BluetoothMerge
{
    public const string WindowsSource = "Windows (Bluetooth)", GattSource = "BLE Battery Service";

    /// <param name="nodeLevels">Battery levels Windows stored on this container's device nodes (may contain junk).</param>
    /// <param name="gattLevel">Level read from the GATT Battery Service, if subscribed.</param>
    /// <param name="alreadyListed">Keep listing a device that once reported a battery, even if it reports none now.</param>
    /// <returns>null when the device has never reported a battery: devices without one are not listed.</returns>
    public static MergedDevice? Merge(string deviceId, string providerId, IReadOnlyList<Endpoint> endpoints,
        IEnumerable<int?> nodeLevels, int? gattLevel, bool alreadyListed, DateTimeOffset now)
    {
        if (endpoints.Count == 0) return null;

        bool connected = endpoints.Any(e => e.Connected);
        // Classic endpoints carry the user-visible name ("WH-1000XM5"); LE ones are sometimes shorter.
        var main = endpoints.FirstOrDefault(e => !e.IsLe) ?? endpoints[0];

        int? level = nodeLevels.Where(l => l is >= 0 and <= 100).Max();
        string source = WindowsSource;
        if (level is null && gattLevel is >= 0 and <= 100) { level = gattLevel; source = GattSource; }
        if (level is null && !alreadyListed) return null;

        var category = Categorize(endpoints, main.Name, out var confidence);
        var info = new DeviceInfo(deviceId, main.Name, null, category, confidence, providerId);
        var reading = level is int p ? new BatteryReading(p, BatteryReading.LevelFromPercent(p), ChargeState.Unknown, now, source) : null;
        return new MergedDevice(info, connected, reading);
    }

    public static DeviceCategory Categorize(IEnumerable<Endpoint> endpoints, string name, out Confidence confidence)
    {
        DeviceCategory? c = null;
        foreach (var e in endpoints)
        {
            c ??= e is { CodMajor: int major, CodMinor: int minor } ? DeviceClassifier.FromClassOfDevice(major, minor) : null;
            c ??= e.Appearance is int a ? DeviceClassifier.FromBleAppearance(a) : null;
        }
        // Class of Device cannot tell earbuds from headphones; the name usually can.
        if (c == DeviceCategory.Headphones && DeviceClassifier.FromName(name) == DeviceCategory.Earbuds) c = DeviceCategory.Earbuds;
        confidence = c is null ? Confidence.None : Confidence.Reported;
        return c ?? DeviceCategory.Other;
    }
}
