# Architecture

## Goals

1. Idle in the background at a few MB and near-zero CPU.
2. Add a new device family without touching the core.
3. Feel native on Windows 11, including light/dark taskbars, DPI scaling and the user's own icon visibility choices.

## Processes

```
┌──────────────────────── Voltray.exe (always running) ────────────────────────┐
│  Win32 message loop + NativeAOT, no UI framework                             │
│                                                                              │
│  IBatteryProvider ×N ──► DeviceRegistry ──► TrayManager ──► Shell_NotifyIcon │
│  (background threads)    (thread-safe)      (UI thread)                      │
│                               │                                              │
│                         Settings (JSON)                                      │
└───────────────────────────────┬──────────────────────────────────────────────┘
                                │ named pipe (planned)
┌───────────────────────────────┴──────────────────────────────────────────────┐
│ Voltray.Settings.exe (planned): WinUI 3, started on demand, exits on close   │
│ Dashboard · Settings · About                                                 │
└──────────────────────────────────────────────────────────────────────────────┘
```

Why two processes: in [phase 0](phase0.md), a NativeAOT WinUI 3 window cost ~107 MB working set, and that memory stayed allocated after the window closed. The Win32 host costs ~5 MB private. Keeping WinUI out of the background process keeps the tray cheap, and a ~300 ms cold start for the settings window is acceptable.

## Components

| Component | Responsibility |
|---|---|
| `IBatteryProvider` | Discovers its own devices, reports readings, pushes events. Only it knows its topology (e.g. 6 slots behind a receiver). Declares `MinimumPollInterval`. |
| `IDeviceSink` / `DeviceRegistry` | Single source of truth. Applies classification and user category overrides, and keeps the last reading when a device disconnects. Raises `Changed`. |
| `DeviceClassifier` | Fallback category guess from the device name. It runs only when the provider could not report a category. |
| `TrayManager` | One `Shell_NotifyIcon` per category with a **fixed GUID**, so Windows remembers each icon's visibility. Renders icons at the current DPI, builds tooltips (≤127 chars) and recreates icons after Explorer restarts. |
| `IconRenderer` | GDI grayscale-AA text and glyphs, cropped and resampled to 16–32 px. Has two styles: percentage, or category glyph with a level bar. |
| `Settings` | `%AppData%\Voltray\settings.json`, using source-generated `System.Text.Json`. |
| `Log` | `%LocalAppData%\Voltray\voltray.log`. |

## Category classification (in priority order)

1. **User override** (`Confidence.User`)
2. **Provider-reported** (`Confidence.Reported`): HID++ device type, Bluetooth Class of Device / BLE Appearance, HID top-level usage (Generic Desktop mouse 0x02 / keyboard 0x06 / gamepad 0x05)
3. **Name heuristics** (`Confidence.Guess`): "Buds", "WF-", "WH-", "Keychron"...
4. Otherwise **Other**. Devices are never dropped for being unclassified.

## Windows tray constraints we design around

- The **user** decides whether an icon is pinned or in the overflow. Apps cannot (and must not) change that. Voltray uses one stable GUID per category, so the choice sticks. While the exe is unsigned, Windows ties the GUID to the exe path.
- **Standard tooltip:** plain text, max 128 chars, `\n` line breaks. Rich hover content would need a custom popup via `NIN_POPUPOPEN` (planned).
- Icons are 16 px at 100 % scale (20/24/32 px at higher scales). Two digits are the readable maximum, so 100 % is shown as "F".
- `TaskbarCreated` is broadcast when Explorer restarts. Every icon must be re-added.

## Threading

Providers run on the thread pool and write to `DeviceRegistry` under a lock. `Changed` posts a single coalesced `WM_APP` message to the tray window, and all `Shell_NotifyIcon` calls happen on the UI thread.
