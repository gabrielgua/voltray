# Adding a provider

A provider teaches Voltray to read batteries from one device family (a vendor protocol, Bluetooth, GameInput...).

## 1. Create the project

```bash
dotnet new classlib -n Voltray.Providers.Acme -o src/Voltray.Providers.Acme
dotnet sln add src/Voltray.Providers.Acme
```

Reference `Voltray.Core` and, if you need HID or other Win32 calls, `Voltray.Platform`. `Directory.Build.props` already marks libraries as AOT-compatible.

## 2. Implement `IBatteryProvider`

```csharp
public sealed class AcmeProvider : IBatteryProvider
{
    IDeviceSink? _sink;

    public string Id => "acme";
    public TimeSpan MinimumPollInterval => TimeSpan.FromMinutes(5);

    public async Task StartAsync(IDeviceSink sink, CancellationToken ct)
    {
        _sink = sink;
        // Discover devices. For each one:
        sink.Upsert(new DeviceInfo(
            Id: "acme:SERIAL",            // stable across restarts and reconnects
            Name: "Acme Headset",
            Manufacturer: "Acme",
            Category: DeviceCategory.Headphones,
            CategoryConfidence: Confidence.Reported,   // or None and let the classifier guess
            ProviderId: Id));
        await RefreshAsync(ct);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        // Read each device and report it:
        _sink!.Report("acme:SERIAL", new BatteryReading(
            Percent: 70, Level: BatteryLevel.Good, Charge: ChargeState.Discharging,
            Timestamp: DateTimeOffset.Now, Source: "Acme HID report 0x07"));
        // Or, if it stopped answering:
        // _sink!.Disconnected("acme:SERIAL");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

## 3. Register it

Add it to `Providers` in [`src/Voltray/Program.cs`](../src/Voltray/Program.cs).

## Guidelines

- **IDs must be stable.** Use a serial or unit ID when the device has one, and fall back to the transport path plus slot.
- **Prefer events over polling.** If the device pushes battery changes, listen for them and keep `RefreshAsync` cheap.
- **Report what you know.** Coarse-only devices: `Percent = null` and a `Level`. Voltage-only: estimate with `VoltageCurve` and say "estimated" in `Source`.
- **Coexist.** Open HID handles in shared mode, and do not claim exclusive access that would break vendor apps.
- **Fail softly.** Catch device errors inside the provider and log them with `Log.Error`. One broken device must not stop the others.
- **Document the protocol** in `docs/protocols/<vendor>.md`, using clean-room sources only (see CONTRIBUTING).
- Add a probe command under `tools/` if users will need to send you diagnostics.
