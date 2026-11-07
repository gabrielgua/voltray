# Contributing to Voltray

Thanks for helping! The most valuable contributions are **device reports** and **new providers**.

## Reporting a device

1. Run `HidppProbe.exe --list` for Logitech devices, or `BluetoothProbe.exe` for Bluetooth devices (it listens for 15 s, so connect or disconnect the device while it runs).
2. Open a [device support issue](../../issues/new?template=device-support.yml) with the output, the exact device model and how it connects (receiver, Bluetooth, cable).

## Development setup

- Windows 10 19041+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022+ or Build Tools with the **Desktop development with C++** workload (NativeAOT needs the MSVC linker)

```bash
dotnet build
dotnet test
dotnet run --project src/Voltray
```

Logs are written to `%LocalAppData%\Voltray\voltray.log`.

## Project layout

| Path | What |
|---|---|
| `src/Voltray.Core` | Models, `IBatteryProvider`, `DeviceRegistry`, classification. No Windows APIs. |
| `src/Voltray.Platform` | Win32 interop shared by providers (HID, SetupAPI). |
| `src/Voltray.Providers.*` | One project per device family or vendor. |
| `src/Voltray` | The background tray host (`Voltray.exe`). |
| `tools/` | Diagnostic tools (`HidppProbe`, `BluetoothProbe`). |
| `tests/` | Unit tests. |
| `docs/` | Architecture, protocols, measurements. |

## Rules of thumb

- **Stay NativeAOT-compatible.** No reflection-based serialization (use source-generated `System.Text.Json`), no `Assembly.Load`, and use `LibraryImport` for interop. `dotnet publish src/Voltray -c Release -r win-x64` must produce zero warnings. For WinRT, follow the CsWinRT notes in [docs/protocols/bluetooth.md](docs/protocols/bluetooth.md#nativeaot-notes-cswinrt), and **run the published exe**: some WinRT marshalling errors only show up at runtime.
- **Be honest about data.** If a device only reports coarse levels or voltage, say so (`BatteryReading.Percent = null`, or a source marked "estimated").
- **Respect device power saving.** Prefer device events over polling, and declare a sensible `MinimumPollInterval`.
- **Clean-room protocols.** Implement from public documentation or your own captures of your own devices. Do not copy code from GPL projects (Solaar, OpenRazer, HeadsetControl) into this MIT codebase.
- Match the existing style (`.editorconfig`). Keep comments for the *why*.

## Pull requests

Keep PRs focused, add tests for logic in `Voltray.Core`, and describe which hardware you tested on.
