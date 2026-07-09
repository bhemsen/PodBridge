# Research: WinRT paired/connected AirPods detection

> Permanent record for the `chore:research-connection-detection` issue (#4).
> Authority for the Phase-1 implementation issue (#6). Clean-room: this file
> cites Microsoft Learn / official WinRT reference only — no GPL source or
> verbatim protocol-doc prose is reproduced.
>
> Scope: detecting whether an **AirPods (Bluetooth Classic)** device is **paired**
> and **connected** on Windows 11 via WinRT, watching that state live, and
> degrading gracefully when no Bluetooth radio is present. BLE-advertisement
> telemetry (battery / in-ear) is **Phase 2**, not this path.

## Sources

1. [BluetoothDevice.GetDeviceSelectorFromPairingState (Windows.Devices.Bluetooth)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfrompairingstate?view=winrt-26100)
   — the AQS selector for enumerating **paired** (or unpaired) Bluetooth **Classic**
   devices; passed to `DeviceInformation.FindAllAsync` / `DeviceInformation.CreateWatcher`.
2. [BluetoothDevice.ConnectionStatus (Windows.Devices.Bluetooth)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.connectionstatus?view=winrt-26100)
   — the `BluetoothConnectionStatus` (`Connected` / `Disconnected`) property.
3. [BluetoothDevice.ConnectionStatusChanged event (Windows.Devices.Bluetooth)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.connectionstatuschanged?view=winrt-26100)
   — the live connect/disconnect event; signature `TypedEventHandler<BluetoothDevice, object>`.
4. [System.Devices.Aep.IsConnected (Win32 device properties)](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-aep-isconnected)
   — the Boolean AEP property key used to observe connection state through a `DeviceWatcher`.
5. [UWP: Working with Bluetooth devices (part 1) — Microsoft Learn archive](https://learn.microsoft.com/en-us/archive/blogs/cdndevs/uwp-working-with-bluetooth-devices-part-1)
   — the `DeviceInformation.CreateWatcher(aqs, additionalProperties, DeviceInformationKind.AssociationEndpoint)`
   pattern with `System.Devices.Aep.DeviceAddress` / `System.Devices.Aep.IsConnected` and Added/Updated/Removed handlers.
6. [Radio.GetRadiosAsync (Windows.Devices.Radios)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.getradiosasync?view=winrt-26100)
   — snapshot of radios; filter `RadioKind.Bluetooth`; the empty-result / desktop-architecture caveats.
7. [BluetoothAdapter.GetDefaultAsync (Windows.Devices.Bluetooth)](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.getdefaultasync?view=winrt-26100)
   — the default adapter (returns `null` when no Bluetooth radio is present); `GetRadioAsync` for on/off.
8. [Microsoft Q&A: How to discover when a Bluetooth connection disconnects](https://learn.microsoft.com/en-us/answers/questions/5571988/how-to-discover-when-bluetooth-connection-disconne)
   — Microsoft-answered guidance: subscribe to `ConnectionStatusChanged`; no disconnect reason is exposed.

## Consensus

### Paired-device enumeration

- **API:** `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` returns an
  Advanced Query Syntax (AQS) selector string for **paired** Bluetooth **Classic**
  devices (pass `false` for unpaired). Namespace `Windows.Devices.Bluetooth`.
  Introduced Windows 10 10.0.10586; requires the `bluetooth` app capability.
  (Source 1.)
- **Snapshot:** `DeviceInformation.FindAllAsync(selector)` → `DeviceInformationCollection`.
- **Live:** `DeviceInformation.CreateWatcher(selector)` → `DeviceWatcher`.
  Both in `Windows.Devices.Enumeration`.
- AirPods enumerate as a **Bluetooth Classic** device (A2DP/HFP), so use
  `BluetoothDevice.*`, not `BluetoothLEDevice.*`. Phase-1 identification is a name
  heuristic (`DeviceInformation.Name` contains "AirPods"/"Beats") per the spec.

### Connection status + change event

- **State:** `BluetoothDevice.ConnectionStatus` → `BluetoothConnectionStatus`
  enum, values `Connected` / `Disconnected`. (Source 2.)
- **Change event:** `BluetoothDevice.ConnectionStatusChanged`, C# signature
  `event TypedEventHandler<BluetoothDevice, object>`. In the handler, read the
  connection state from **`sender.ConnectionStatus`** — the second argument is a
  bare `object` / `IInspectable` (no typed event-args). (Source 3.)
- **Obtain the instance:** `BluetoothDevice.FromIdAsync(deviceInformation.Id)`.
- **Lifetime caveat:** keep a live reference to each `BluetoothDevice` for as long
  as you want events — if it is garbage-collected the event stops firing.
  (Sources 3, 8.)
- The API exposes **no disconnect reason** (out-of-range vs powered-off vs
  user-disconnect are indistinguishable). (Source 8.)

### Live watcher + property keys

- **Watcher:** `DeviceInformation.CreateWatcher(aqsFilter, additionalProperties,
  DeviceInformationKind.AssociationEndpoint)`. (Source 5.)
- **Requested property keys** (the `additionalProperties` string array):
  - `System.Devices.Aep.IsConnected` — Boolean; "Whether the device is currently
    connected to the system or not." Key: formatID `A35996AB-11CF-4935-8B61-A6761081ECDF`,
    propID `7` (`PKEY_Devices_Aep_IsConnected`). Available since Windows 10 1507. (Source 4.)
  - `System.Devices.Aep.DeviceAddress` — the Bluetooth MAC address, for matching a
    specific device. (Source 5.)
- **Events:** `Added` (`DeviceInformation`), `Updated` (`DeviceInformationUpdate` —
  connection-state changes arrive here as an update to `System.Devices.Aep.IsConnected`),
  `Removed` (`DeviceInformationUpdate`). Practical gotcha from Source 5: assign a
  `Removed` handler (even an empty one) or the watcher may not start reliably.

### No-radio / "Bluetooth unavailable" detection

- **Primary:** `Radio.GetRadiosAsync()` (`Windows.Devices.Radios`) returns a
  snapshot `IReadOnlyList<Radio>`; select `r.Kind == RadioKind.Bluetooth` and read
  `r.State` (`On` / `Off` / `Disabled` / `Unknown`). No Bluetooth radio present →
  no `RadioKind.Bluetooth` entry. (Source 6.)
- **Complementary:** `BluetoothAdapter.GetDefaultAsync()` (`Windows.Devices.Bluetooth`)
  completes with `null` when there is no Bluetooth adapter; `adapter.GetRadioAsync()`
  then yields the radio for on/off. (Source 7.)
- **Critical desktop caveat (Source 6):** from a **Win32 desktop process**,
  `Radio.GetRadiosAsync()` returns radios **only when the process architecture
  matches the OS architecture** (x64 process on x64 OS, ARM64 on ARM64). An **x86
  process on x64/ARM64 yields an empty list**, indistinguishable from "no radio".
  → PodBridge must ship as **x64 (and/or ARM64), never x86** (do not force
  32-bit), or the no-radio state will be spuriously reported.
- An empty result is not an error — treat empty list / `null` adapter as the
  benign **"Bluetooth unavailable"** tray state, never a crash.

### Cross-Windows-11-build reliability

- The API surface is **stable across every Windows 10/11 build** (Microsoft Learn
  lists these members for moniker ranges from `winrt-10586` through `winrt-28000`,
  covering Windows 11 21H2 / 22H2 / 23H2 / 24H2 without breaking changes). No
  build-specific API divergence was found.
- The real reliability risks are **behavioural, not API-version**: (a) the GC
  lifetime of `BluetoothDevice` (hold references), (b) the x86-process radio
  enumeration footgun, (c) no disconnect reason. These are handled by the
  recommended approach below, and the whole surface sits behind
  `IConnectionMonitor` with a fake for device-independent unit tests per the spec.

## Recommended approach (for issue #6)

Use a **two-part** strategy, both behind `IConnectionMonitor`:

1. **A `DeviceWatcher`** created from
   `BluetoothDevice.GetDeviceSelectorFromPairingState(true)` (paired Classic
   devices), with `DeviceInformationKind.AssociationEndpoint` and
   `additionalProperties = { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress" }`.
   It robustly surfaces **pairing/appearance/removal** (pair, unpair, radio
   toggled) and carries the initial `IsConnected` value. Assign `Added`, `Updated`,
   and `Removed` (Updated carries live `IsConnected` changes; keep a non-null
   `Removed` handler).
2. **Per matched AirPods device, a held `BluetoothDevice`** obtained via
   `BluetoothDevice.FromIdAsync(id)` with a `ConnectionStatusChanged` subscription,
   reading `sender.ConnectionStatus` for the authoritative live **connect/disconnect**
   signal. Keep the reference alive for the device's lifetime; unsubscribe and
   release on `Removed`.
3. **Before either**, gate on radio presence: `Radio.GetRadiosAsync()` (or
   `BluetoothAdapter.GetDefaultAsync() == null`); if no `RadioKind.Bluetooth` /
   null adapter, emit the **"Bluetooth unavailable"** state and skip the watcher.
   Ensure the app builds/runs as **x64/ARM64** so radio enumeration is valid.

Map to tray state: radio absent → "Bluetooth unavailable"; radio present but no
paired AirPods → pairing-guidance state; paired + `ConnectionStatus == Connected`
(or `Aep.IsConnected == true`) → "connected"; paired + `Disconnected` → "disconnected".
Requires the `bluetooth` app capability; no elevation (`asInvoker`).

## Disputes (minority → majority decision)

- **`ConnectionStatusChanged` handler shape:** the Microsoft Q&A sample (Source 8)
  reads `e.BluetoothConnectionStatus` from the event args, but the official event
  reference (Source 3) defines the signature as `TypedEventHandler<BluetoothDevice, object>`
  — the second parameter is a bare `object`/`IInspectable` with no such property.
  Sources 2+3 (reference) over Source 8 (sample) → **read `sender.ConnectionStatus`
  in the handler, not an event-arg property.**
- **Best live-connection monitor:** Source 8 recommends `ConnectionStatusChanged`
  only; Sources 4+5 support a `DeviceWatcher` over `System.Devices.Aep.IsConnected`.
  Not truly contradictory → **use both**: a `DeviceWatcher(AssociationEndpoint)` for
  pairing/presence + robustness, and a held-`BluetoothDevice` `ConnectionStatusChanged`
  for the authoritative connect/disconnect edge.
- **No-radio check:** the search surfaced both `Radio.GetRadiosAsync()` and
  `BluetoothAdapter.GetDefaultAsync() == null`. Both are valid → **prefer
  `Radio.GetRadiosAsync()`** (it also yields on/off state), treating an empty list
  **or** a null adapter as "Bluetooth unavailable", and mind the x86-process caveat.
