# Voltray

**Battery levels for all your wireless peripherals, right in the Windows system tray.**

Voltray shows the battery of your mouse, keyboard, headphones, earbuds and controllers next to the Windows clock: one icon per device category, or one per device. It is a small native background app with no vendor software required.

> **Status: early development (v0.1.0-dev).** Logitech HID++ works and is tested on hardware. Bluetooth support is implemented but not yet verified with a real device ([help wanted](#device-support)). See the [roadmap](#roadmap).

![The four tray icon styles at 16, 24 and 32 px, on dark and light taskbars](docs/images/tray-icons.png)

| Devices | Settings | Notifications |
|---|---|---|
| ![Devices page](docs/images/window-devices.png) | ![Settings page](docs/images/window-settings.png) | ![Notification settings](docs/images/window-notifications.png) |

## Features

- **Tray icons that tell devices apart.** Every icon shows the device's symbol ([Hugeicons](https://hugeicons.com), stroke rounded). Choose one of four styles:
  - **Bar:** rounded level bar below the symbol.
  - **Vertical bar:** rounded level bar beside the symbol.
  - **Colored:** the symbol itself turns green, yellow, orange or red with the level.
  - **Number:** symbol on top, percentage below in a pixel font that stays readable at 16 px. The "%" sign and level-colored digits are optional.
- **One icon per category or one per device.** Per category, two mice share the mouse icon and it shows the lowest battery. Per device, each one gets its own icon, and you can pick that icon from 22 symbols.
- **Disconnected devices** stay in the tray dimmed with their last level, or are hidden. They are always listed in the window.
- **Hover** shows the devices and their levels. **Left click** opens the Voltray window; **right click** opens a quick menu (icon style, hide an icon, refresh).
- **Low-battery notifications:** any number of rules, each with its own limit and message, plus a test button. Each rule warns once per discharge; crossing several at once shows only the lowest. Never while charging. Optionally, a notice when charging completes. Each device can be muted. Windows Do Not Disturb, including the automatic mode during full-screen games, is respected.
- **Settings window** (Windows 11 style, light/dark, acrylic): every device with its level, connection state and source; per-device "show in tray", notifications, category and icon; start with Windows; refresh interval.
- **Coexists with vendor software.** Logitech devices are read directly over HID++, with or without G HUB running.
- **Tiny footprint in the background:** ~5 MB executable, ~9 MB private memory, ~0 % CPU when idle. The settings window is a separate process that exits when closed.

## Device support

Voltray only shows what a device reports. Some devices never report battery to the PC, and no software can read it from them.

| Source | Status | Notes |
|---|---|---|
| Logitech LIGHTSPEED / Unifying / Bolt receivers (HID++ 2.0) | ✅ Working | Exact percentage on newer devices (feature 0x1004). Estimated from voltage on older ones. Tested: PRO X Superlight 2. |
| Bluetooth Classic headsets (HFP) | 🧪 Implemented, untested | Same level Windows shows in Settings, pushed on change. |
| Bluetooth LE | 🧪 Implemented, untested | Windows-read level, or the GATT Battery Service with notifications as fallback. |
| Xbox controllers (GameInput) | 🗓 Planned | |
| DualSense / DualShock | 🗓 Planned | |
| Razer, SteelSeries, HyperX, Corsair | 🔬 Needs research | Proprietary protocols, one provider per vendor. |
| Generic 2.4 GHz dongles | ❌ Not possible | They do not send battery information. |

Your device is not detected? Run `HidppProbe.exe --list` (Logitech) or `BluetoothProbe.exe` (Bluetooth) and open a [device support issue](../../issues/new?template=device-support.yml). Bluetooth reports are especially welcome right now.

## Install

There is no release yet. CI builds a ready-to-run folder on every push (Actions → latest run → **Voltray-win-x64**), or build from source:

```bash
git clone https://github.com/gabrielgua/voltray
cd voltray
dotnet publish src/Voltray -c Release -r win-x64 -o artifacts/publish/Voltray
dotnet publish src/Voltray.Settings -c Release -o artifacts/publish/Voltray/settings
```

Run `artifacts/publish/Voltray/Voltray.exe`. The `settings` folder must stay next to it.

Requirements: Windows 10 2004 (19041)+ or Windows 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download), and the Visual Studio C++ build tools (for NativeAOT).

### Why is my icon hidden?

New tray icons go to the overflow (^) area by default. Windows leaves that choice to you, and Voltray does not override it. To pin an icon, open **Settings → Personalization → Taskbar → Other system tray icons** and turn Voltray on. Each Voltray icon (each category, or each device) has its own entry there.

## Architecture

```
Voltray.exe (background, Win32 + NativeAOT, ~5 MB)
 ├─ Providers ── Logitech HID++ · Bluetooth · ...   report devices + readings
 ├─ DeviceRegistry ── classification, user overrides, last-known state
 ├─ BatteryAlerts ── low-battery rules
 └─ TrayManager ── icons per category or per device, tooltips, menus
       └─ Voltray.Rendering ── tray icon renderer (also used by the window)

Voltray.Settings.exe (WinUI 3, started on demand, exits when closed) ◄── named pipe ──► Voltray.exe
```

The settings window runs as a separate process because WinUI 3 keeps ~100 MB in memory after its window closes. See [docs/architecture.md](docs/architecture.md) and the [phase 0 measurements](docs/phase0.md).

## Roadmap

- **Done:** Logitech HID++, Bluetooth HFP + BLE (untested on hardware), settings window, start with Windows, notification rules, four icon styles, per-device tray icons.
- **Next:** battery history and estimated time remaining, smoother levels for devices that report in 10 % steps, native Windows notifications with the device image and an "urgent" priority, a better icon picker.
- **Later:** a compact window centered on the device list, a quick tray panel, Xbox / PlayStation / Switch controllers, Razer and SteelSeries receivers, a 7-day history chart, English, installer and winget.

## Contributing

Contributions are welcome, especially new device providers and device reports. Read [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/adding-a-provider.md](docs/adding-a-provider.md).

## License

[MIT](LICENSE). Icons from the [Hugeicons](https://hugeicons.com) free set, MIT ([license](assets/icons/LICENSE-hugeicons.md)). Voltray is not affiliated with Logitech or any other device manufacturer. Product names are trademarks of their owners.
