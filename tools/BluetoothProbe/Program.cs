using Voltray.Core;
using Voltray.Providers.Bluetooth;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

// Diagnostic tool: dumps paired Bluetooth endpoints, device nodes that carry a battery level,
// and what the Bluetooth provider reports. Attach its output to "device not detected" issues.
static string Props(DeviceInformation d) =>
    string.Join("  ", d.Properties.Where(p => p.Value != null).Select(p => $"{p.Key.Split('.')[^1]}={p.Value}"));

// Sanity check: the instance-id prefix AQS form must match HID nodes on any PC with a keyboard.
var hid = await DeviceInformation.FindAllAsync("System.Devices.DeviceInstanceId:~<\"HID\\\"", Array.Empty<string>(), DeviceInformationKind.Device);
Console.WriteLine($"AQS check: {hid.Count} HID device nodes (should be > 0)");

foreach (var (label, selector) in new[]
{
    ("Classic", BluetoothDevice.GetDeviceSelectorFromPairingState(true)),
    ("LE", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)),
})
{
    var list = await DeviceInformation.FindAllAsync(selector, BluetoothProvider.EndpointProperties, DeviceInformationKind.AssociationEndpoint);
    Console.WriteLine($"\nPaired {label} endpoints: {list.Count}");
    foreach (var d in list) Console.WriteLine($"  {d.Name}\n    {Props(d)}");
}

var nodes = await DeviceInformation.FindAllAsync(BluetoothProvider.BatteryNodeSelector,
    new[] { BluetoothProvider.BatteryKey, BluetoothProvider.ContainerKey }, DeviceInformationKind.Device); // string[], not [..]: CsWinRT AOT needs a known type
var withBattery = nodes.Where(n => n.Properties.TryGetValue(BluetoothProvider.BatteryKey, out var v) && v != null).ToList();
Console.WriteLine($"\nBluetooth device nodes: {nodes.Count}, with a Windows battery level: {withBattery.Count}");
foreach (var n in withBattery)
    Console.WriteLine($"  {n.Name}: {n.Properties[BluetoothProvider.BatteryKey]}% ({n.Properties[BluetoothProvider.BatteryKey]?.GetType().Name})  container={n.Properties[BluetoothProvider.ContainerKey]}");

Console.WriteLine("\nProvider (listening 15 s; connect/disconnect a device to see events):");
var registry = new DeviceRegistry();
registry.Changed += () =>
{
    foreach (var d in registry.Snapshot())
        Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {d.Info.Name} — {d.Info.Category} ({d.Info.CategoryConfidence}) — " +
            $"{(d.Connected ? "connected" : "disconnected")} — {d.Battery?.Percent?.ToString() ?? "?"}% via {d.Battery?.Source ?? "-"}");
};
await using var provider = new BluetoothProvider();
await provider.StartAsync(registry, CancellationToken.None);
if (registry.Snapshot().Count == 0) Console.WriteLine("  (no Bluetooth device with battery information)");
await Task.Delay(TimeSpan.FromSeconds(args.Contains("--quick") ? 0 : 15));
