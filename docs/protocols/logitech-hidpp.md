# Logitech HID++ 2.0

How `Voltray.Providers.Logitech` reads battery levels. Sources: Logitech's public HID++ 2.0 feature documentation, and USB captures of our own devices.

## Transport

Receivers and HID++ devices expose vendor-defined HID collections:

| Collection | Usage page : usage | Report | Size | Carries |
|---|---|---|---|---|
| Short | `FF00:0001` | `0x10` | 7 B | HID++ 1.0: receiver registers, errors, connection notifications |
| Long | `FF00:0002` | `0x11` | 20 B | HID++ 2.0 requests and replies |
| Bluetooth / Bolt direct | `FF43:0202` | `0x11` | 20 B | HID++ 2.0 |

Both collections of one USB interface have paths that differ in `&col0N` **and** in the last segment of the instance ID (`…&0&0000` / `…&0&0001`). Pair them after stripping both.

Handles are opened with `FILE_SHARE_READ | FILE_SHARE_WRITE`. Every process that has the collection open receives every reply. Requests carry a **software ID** in the low nibble of byte 3. Voltray uses `0xD` and ignores replies with other IDs, which is how it coexists with G HUB.

## Message format (long)

```
11  dev  feat  (func<<4 | swid)  params[16]
```

- `dev`: receiver slot `1..6`, or `0xFF` for a directly connected device.
- `feat`: feature **index**, which is per device. Resolve it with IRoot.
- Errors: `11 dev FF feat func|swid code` (HID++ 2.0), or `10 dev 8F feat func|swid code` on the short collection (HID++ 1.0).
  - `0x08` unknown device = empty slot
  - `0x09` resource error = paired but unreachable

## Features used

| ID | Name | Function | Reply |
|---|---|---|---|
| `0x0000` | IRoot | 0 `getFeature(id)` | `[index, type, version]`. Index 0 means not supported. |
| | | 1 `ping(0,0,data)` | `[major, minor, data]`, protocol version |
| `0x0003` | DeviceInformation | 0 `getDeviceInfo` | `[entities, unitId×4, …]`. The unit ID is the stable device ID. |
| `0x0005` | DeviceName | 0 count, 1 `name(offset)`, 2 type | Type: 0 keyboard, 3 mouse, 4 trackpad, 5 trackball, 8 headset… |
| `0x1004` | UnifiedBattery | 1 `getStatus` | `[percent, levelFlags, chargingStatus, externalPower]` |
| `0x1001` | BatteryVoltage | 0 `getBatteryInfo` | `[mV hi, mV lo, flags]`. Needs a voltage→percent curve. |
| `0x1000` | BatteryStatus | 0 `getStatus` | `[percent, nextLevel, status]` |

Charging status (0x1004 / 0x1000): 0 discharging, 1 charging, 2 slow charging, 3 complete, 4 error.

### Observed quirks

- PRO X Superlight 2 (`PRO X Wireless`, receiver `046D:C547`): HID++ 4.2, battery via 0x1004. Level flags said "full" at 65 %, so **trust the percentage, not the flags**.
- A sleeping mouse takes ~0.9 s to answer a ping, which is why the ping timeout is 3 s. Slots that fail are retried on every refresh.

## Notifications

- Battery features send events with software ID 0 when the level changes. Voltray re-reads that device.
- HID++ 1.0 `0x41` (device connection) on the short collection: bit 6 of byte 4 set means the link was lost.
