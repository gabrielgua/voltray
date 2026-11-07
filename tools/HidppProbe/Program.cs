using System.Diagnostics;
using Voltray.Platform.Hid;
using Voltray.Providers.Logitech;

// Diagnostic tool: dumps Logitech HID collections and reads every HID++ device's battery.
// Attach its output to "device not detected" issues.
Console.WriteLine($"G HUB running: {Process.GetProcessesByName("lghub_agent").Length > 0}");

if (args.Contains("--list"))
{
    Console.WriteLine("\nLogitech HID collections:");
    foreach (var c in HidNative.EnumerateCollections(0x046D))
        Console.WriteLine(c.Error != 0
            ? $"  [error {c.Error}] {c.Path}"
            : $"  {c.Vid:X4}:{c.Pid:X4} usage {c.UsagePage:X4}:{c.Usage:X4} in={c.InputLen} out={c.OutputLen}  {c.Path}");
}

var endpoints = HidppTransport.Discover();
Console.WriteLine($"\nHID++ endpoints: {endpoints.Count}");

foreach (var ep in endpoints)
{
    Console.WriteLine($"\n== {ep.Vid:X4}:{ep.Pid:X4}  long={ep.LongLen}B short={(ep.ShortPath != null ? ep.ShortLen + "B" : "-")}");
    using var t = new HidppTransport(ep);
    t.Notification += r => Console.WriteLine($"   [event] {Convert.ToHexString(r)}");

    foreach (byte slot in new byte[] { 1, 2, 3, 4, 5, 6, 0xFF })
    {
        var dev = new HidppDevice(t, slot);
        var sw = Stopwatch.StartNew();
        if (!await dev.PingAsync()) continue;
        Console.WriteLine($" slot 0x{slot:X2}: HID++ {dev.ProtocolVersion:0.0} (ping {sw.ElapsedMilliseconds} ms)");
        if (dev.ProtocolVersion < 2) continue; // the receiver itself answers HID++ 1.0
        try
        {
            await dev.LoadIdentityAsync();
            Console.WriteLine($"   name: {dev.Name}  kind: {dev.Kind}  unit: {dev.UnitId ?? "-"}");
            foreach (ushort f in new ushort[] { HidppDevice.FeatUnifiedBattery, HidppDevice.FeatBatteryVoltage, HidppDevice.FeatBatteryStatus })
                Console.WriteLine($"   feature 0x{f:X4}: {(await dev.FeatureIndexAsync(f) is byte i ? $"index {i}" : "no")}");
            sw.Restart();
            var b = await dev.ReadBatteryAsync();
            Console.WriteLine(b is null ? "   battery: not supported"
                : $"   battery: {(b.Percent is int p ? p + "%" : "n/a")} {b.Level} {b.Charge} via {b.Source} ({sw.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex) { Console.WriteLine($"   error: {ex.Message}"); }
    }
}

if (args.Contains("--listen"))
{
    Console.WriteLine("\nListening for events for 30 s (turn the device off/on, plug the charger)...");
    using var listeners = new CompositeDisposable(endpoints.Select(ep =>
    {
        var t = new HidppTransport(ep);
        t.Notification += r => Console.WriteLine($"   [{DateTime.Now:HH:mm:ss}] {Convert.ToHexString(r)}");
        return (IDisposable)t;
    }).ToList());
    await Task.Delay(30_000);
}

sealed class CompositeDisposable(List<IDisposable> items) : IDisposable
{
    public void Dispose() => items.ForEach(i => i.Dispose());
}
