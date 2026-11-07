using Voltray.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Voltray.Providers.Bluetooth;

/// Paired Bluetooth devices (Classic and LE).
///
/// Battery comes from two sources, merged per physical device (device container):
/// 1. The level Windows itself reads (HFP battery indicator on headsets, BLE Battery Service
///    on many LE devices) and stores on a device node. This is what Settings shows and needs
///    no connection of our own. We watch it, so changes are pushed.
/// 2. For LE devices without (1), the GATT Battery Service read directly, with notifications.
public sealed partial class BluetoothProvider : IBatteryProvider
{
    /// Battery percentage Windows stores on a Bluetooth device node (DEVPROP_TYPE_BYTE).
    public const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    public const string ContainerKey = "System.Devices.ContainerId";
    public const string AepContainer = "System.Devices.Aep.ContainerId", AepConnected = "System.Devices.Aep.IsConnected",
        CodMajor = "System.Devices.Aep.Bluetooth.Cod.Major", CodMinor = "System.Devices.Aep.Bluetooth.Cod.Minor",
        LeAppearance = "System.Devices.Aep.Bluetooth.Le.Appearance";

    /// Bluetooth Classic and LE device nodes. AQS cannot filter on a "{GUID} pid" key (it falls back to
    /// full-text search and fails), so we select by instance-id prefix and read the battery key as a property.
    public const string BatteryNodeSelector =
        "System.Devices.DeviceInstanceId:~<\"BTHENUM\\\" OR System.Devices.DeviceInstanceId:~<\"BTHLE\"";
    // Real string[] arrays (not collection expressions): CsWinRT AOT can only marshal types it generated code for.
    public static readonly string[] EndpointProperties = new[] { AepContainer, AepConnected, CodMajor, CodMinor, LeAppearance };
    public static readonly string[] NodeProperties = new[] { BatteryKey, ContainerKey };

    sealed partial class Gatt(BluetoothLEDevice device, GattDeviceService service, GattCharacteristic level) : IDisposable
    {
        public GattCharacteristic Level { get; } = level;
        public byte? Value;
        public void Dispose() { service.Dispose(); device.Dispose(); }
    }

    readonly Lock _lock = new();
    readonly Dictionary<string, DeviceInformation> _endpoints = new();          // AEP id -> paired endpoint (Classic or LE)
    readonly Dictionary<string, DeviceInformation> _batteryNodes = new();       // Bluetooth device node id -> node (battery key may be empty)
    readonly Dictionary<Guid, Gatt> _gatt = new();                              // container -> GATT battery subscription
    readonly HashSet<Guid> _gattUnavailable = [];                               // tried while connected, no Battery Service
    readonly HashSet<Guid> _published = [];
    readonly List<DeviceWatcher> _watchers = [];
    IDeviceSink? _sink;

    public string Id => "bluetooth";
    public TimeSpan MinimumPollInterval => TimeSpan.FromMinutes(1);

    public async Task StartAsync(IDeviceSink sink, CancellationToken ct)
    {
        _sink = sink;
        var ready = new List<Task>
        {
            Watch(BluetoothDevice.GetDeviceSelectorFromPairingState(true), EndpointProperties, DeviceInformationKind.AssociationEndpoint, _endpoints),
            Watch(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), EndpointProperties, DeviceInformationKind.AssociationEndpoint, _endpoints),
            Watch(BatteryNodeSelector, NodeProperties, DeviceInformationKind.Device, _batteryNodes),
        };
        await Task.WhenAny(Task.WhenAll(ready), Task.Delay(TimeSpan.FromSeconds(10), ct));
        lock (_lock) Log.Info($"bluetooth: {_endpoints.Count} paired endpoint(s), {_batteryNodes.Values.Count(n => Int(n, BatteryKey) != null)} node(s) with a battery level");
        PublishAll();
    }

    /// Starts a watcher that mirrors matching devices into <paramref name="into"/>. Completes after the initial enumeration.
    Task Watch(string selector, string[] properties, DeviceInformationKind kind, Dictionary<string, DeviceInformation> into)
    {
        var enumerated = new TaskCompletionSource();
        var w = DeviceInformation.CreateWatcher(selector, properties, kind);
        w.Added += (_, info) => { lock (_lock) into[info.Id] = info; Changed(info.Id); };
        w.Updated += (_, update) =>
        {
            lock (_lock) { if (into.TryGetValue(update.Id, out var info)) info.Update(update); }
            Changed(update.Id);
        };
        w.Removed += (_, update) => { Guid? c; lock (_lock) { c = ContainerOf(update.Id); into.Remove(update.Id); } if (c is Guid g) Publish(g); };
        w.EnumerationCompleted += (_, _) => enumerated.TrySetResult();
        w.Stopped += (_, _) => enumerated.TrySetResult();
        _watchers.Add(w);
        w.Start();
        return enumerated.Task;
    }

    void Changed(string id)
    {
        Guid? c;
        lock (_lock) c = ContainerOf(id);
        if (c is Guid g) Publish(g);
    }

    Guid? ContainerOf(string id)
    {
        if (_endpoints.TryGetValue(id, out var e)) return Prop<Guid>(e, AepContainer);
        if (_batteryNodes.TryGetValue(id, out var n)) return Prop<Guid>(n, ContainerKey);
        return null;
    }

    static T? Prop<T>(DeviceInformation info, string key) where T : struct =>
        info.Properties.TryGetValue(key, out var v) && v is T t ? t : null;

    /// Numeric properties come back as byte, UInt16 or UInt32 depending on the key and Windows build.
    static int? Int(DeviceInformation info, string key) =>
        info.Properties.TryGetValue(key, out var v) && v is IConvertible c and not string ? c.ToInt32(null) : null;

    void PublishAll()
    {
        List<Guid> containers;
        lock (_lock) containers = [.. _endpoints.Values.Select(e => Prop<Guid>(e, AepContainer)).OfType<Guid>().Distinct()];
        foreach (var c in containers) Publish(c);
    }

    /// Recomputes one physical device from all its endpoints and battery sources, and reports it.
    void Publish(Guid container)
    {
        string deviceId = DeviceId(container);
        MergedDevice? merged;
        string? gattCandidate = null;
        lock (_lock)
        {
            var endpoints = _endpoints.Values.Where(e => Prop<Guid>(e, AepContainer) == container).Select(ToEndpoint).ToList();
            var nodeLevels = _batteryNodes.Values.Where(n => Prop<Guid>(n, ContainerKey) == container).Select(n => Int(n, BatteryKey));
            int? gattLevel = _gatt.TryGetValue(container, out var g) ? g.Value : null;
            merged = BluetoothMerge.Merge(deviceId, Id, endpoints, nodeLevels, gattLevel, _published.Contains(container), DateTimeOffset.Now);
            bool connected = endpoints.Any(e => e.Connected);

            // No Windows-read level: try the GATT Battery Service ourselves (LE only, once per connection).
            if (connected && merged?.Reading is null && !_gatt.ContainsKey(container) && !_gattUnavailable.Contains(container))
                gattCandidate = endpoints.FirstOrDefault(e => e.IsLe)?.Id;
            if (!connected)
            {
                if (_gatt.Remove(container, out var stale)) stale.Dispose(); // re-subscribe on reconnect
                _gattUnavailable.Remove(container);
            }
            if (merged != null) _published.Add(container);
            else if (endpoints.Count == 0 && _published.Contains(container)) _sink!.Disconnected(deviceId); // unpaired
        }

        if (merged != null)
        {
            _sink!.Upsert(merged.Info);
            if (!merged.Connected) _sink.Disconnected(deviceId);
            else if (merged.Reading != null) _sink.Report(deviceId, merged.Reading);
        }
        if (gattCandidate != null) _ = SubscribeGattAsync(container, gattCandidate);
    }

    static Endpoint ToEndpoint(DeviceInformation e) => new(
        e.Id, e.Name, e.Id.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase),
        Prop<bool>(e, AepConnected) == true, Int(e, CodMajor), Int(e, CodMinor), Int(e, LeAppearance));

    static string DeviceId(Guid container) => $"bluetooth:{container:N}";

    async Task SubscribeGattAsync(Guid container, string leId)
    {
        BluetoothLEDevice? dev = null;
        GattDeviceService? svc = null;
        try
        {
            dev = await BluetoothLEDevice.FromIdAsync(leId);
            if (dev is null) return;
            var services = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
            {
                lock (_lock) _gattUnavailable.Add(container);
                Log.Info($"bluetooth: {dev.Name} has no readable Battery Service ({services.Status})");
                dev.Dispose();
                return;
            }
            svc = services.Services[0];
            var chars = await svc.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached);
            if (chars.Status != GattCommunicationStatus.Success || chars.Characteristics.Count == 0)
            {
                lock (_lock) _gattUnavailable.Add(container);
                svc.Dispose(); dev.Dispose();
                return;
            }
            var gatt = new Gatt(dev, svc, chars.Characteristics[0]);
            gatt.Value = await ReadLevelAsync(gatt.Level);
            if (gatt.Level.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                gatt.Level.ValueChanged += (_, e) => { gatt.Value = ReadByte(e.CharacteristicValue); Publish(container); };
                await gatt.Level.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
            }
            lock (_lock)
            {
                if (_gatt.Remove(container, out var old)) old.Dispose();
                _gatt[container] = gatt;
            }
            Log.Info($"bluetooth: {dev.Name} battery via GATT = {gatt.Value?.ToString() ?? "?"}%");
            Publish(container);
        }
        catch (Exception e)
        {
            lock (_lock) _gattUnavailable.Add(container);
            Log.Error($"bluetooth: GATT battery for {leId}", e);
            svc?.Dispose();
            dev?.Dispose();
        }
    }

    static async Task<byte?> ReadLevelAsync(GattCharacteristic c)
    {
        var r = await c.ReadValueAsync(BluetoothCacheMode.Uncached);
        return r.Status == GattCommunicationStatus.Success ? ReadByte(r.Value) : null;
    }

    static byte? ReadByte(IBuffer b) => b.Length > 0 ? DataReader.FromBuffer(b).ReadByte() : null;

    public async Task RefreshAsync(CancellationToken ct)
    {
        // Windows-read levels are pushed by the watcher; only GATT values need polling as a safety net.
        List<(Guid, Gatt)> gatt;
        lock (_lock) gatt = [.. _gatt.Select(kv => (kv.Key, kv.Value))];
        foreach (var (container, g) in gatt)
        {
            try { g.Value = await ReadLevelAsync(g.Level) ?? g.Value; }
            catch (Exception e) { Log.Error("bluetooth: GATT refresh", e); }
            Publish(container);
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var w in _watchers)
            if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop();
        lock (_lock)
        {
            foreach (var g in _gatt.Values) g.Dispose();
            _gatt.Clear();
        }
        return ValueTask.CompletedTask;
    }
}
