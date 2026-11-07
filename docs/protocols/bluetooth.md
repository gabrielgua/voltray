# Bluetooth

How `Voltray.Providers.Bluetooth` finds paired devices and their battery levels.

> **Hardware status:** the enumeration, AQS queries and watchers are verified on Windows 11 26300 under NativeAOT, so far only on a PC with no paired devices. The merge logic is covered by unit tests. Reading an actual headset or BLE device is **not yet verified**. Please report results with `BluetoothProbe.exe`.

## Devices: paired association endpoints (AEP)

Two `DeviceWatcher`s with `DeviceInformationKind.AssociationEndpoint`:

- `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` for Classic (`Bluetooth#Bluetooth<adapter>-<device>`)
- `BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)` for LE (`BluetoothLE#BluetoothLE<adapter>-<device>`)

Requested properties:

| Property | Use |
|---|---|
| `System.Devices.Aep.ContainerId` | Groups the Classic and LE endpoints of one physical device. Voltray's device ID is `bluetooth:<container>`. |
| `System.Devices.Aep.IsConnected` | Connected / disconnected state, pushed as watcher updates. |
| `System.Devices.Aep.Bluetooth.Cod.Major` / `.Minor` | Classic Class of Device → category (Audio/Video headset/headphones, Peripheral keyboard/pointing/gamepad). |
| `System.Devices.Aep.Bluetooth.Le.Appearance` | LE GAP Appearance → category (0x03C1 keyboard, 0x03C2 mouse, 0x0941 earbud, 0x0942–0x0944 headset/headphones/neckband). |

The Class of Device cannot distinguish earbuds from headphones. When it says headphones and the name looks like earbuds ("Buds", "WF-"…), we use Earbuds.

## Battery source 1: the level Windows already reads

Windows reads the battery of hands-free headsets (HFP battery indicator) and of many LE devices (Battery Service), and stores it on a device node as property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (byte, 0–100). This is the value shown in **Settings → Bluetooth & devices**.

- AQS **cannot** filter on a `{GUID} pid` key. `"{…} 2":<>[]` falls back to full-text search and throws "Full text search is not supported". So we watch every Bluetooth device node with `System.Devices.DeviceInstanceId:~<"BTHENUM\" OR System.Devices.DeviceInstanceId:~<"BTHLE"`, request the battery key as an additional property and filter in code.
- Nodes are matched to endpoints through `System.Devices.ContainerId`.
- The watcher reports property changes, so level changes are pushed without polling.

## Battery source 2: GATT Battery Service (LE fallback)

Used only when the device is connected, is LE, and Windows has no level for it:

1. `BluetoothLEDevice.FromIdAsync(aepId)`
2. `GetGattServicesForUuidAsync(GattServiceUuids.Battery, Uncached)` → `GetCharacteristicsForUuidAsync(BatteryLevel)`
3. Read once, then subscribe to notifications (CCCD = Notify) when supported.

If the service is missing or access is denied (Windows may reserve it for HID-over-GATT devices), the device is not retried until it reconnects. Subscriptions are released on disconnect.

## Devices without battery reporting

Speakers, many cheap earbuds and some keyboards never send a level. They are not listed. A device that did report once stays listed (as disconnected / no data) so it does not vanish from the user's view.

## NativeAOT notes (CsWinRT)

- `CsWinRTAotOptimizerEnabled` and `AllowUnsafeBlocks` are required in any project that calls WinRT. Classes that implement interfaces mapped to WinRT (e.g. `IDisposable`, which maps to `IClosable`) must be `partial`.
- Pass **real arrays** (`new[] { … }`) where WinRT expects `IEnumerable<T>`. A C# collection expression (`[…]`) can compile to a synthesized type that CsWinRT has no vtable for, which fails at runtime with "Failed to create a CCW".
