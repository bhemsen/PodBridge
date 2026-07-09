# Research: WinRT BluetoothLEAdvertisementWatcher

> Permanent record for the `chore:research-ble-watcher` issue (#15).
> Authority for the Phase-2 implementation issue (#16, `WinRtBleScanner`).
> Clean-room: this file cites Microsoft Learn / official WinRT reference first and
> cross-checks against independent write-ups — no GPL source or verbatim
> protocol-doc prose is reproduced; all facts are restated in our own words.
>
> Scope: how to receive Apple proximity (Continuity) advertisements on the BLE
> advertisement path with `BluetoothLEAdvertisementWatcher` — scanning mode,
> a company-id `0x004C` manufacturer-data filter, RSSI read, and the
> capability/permission needs of an **unpackaged, `asInvoker` (Medium IL) .NET
> desktop app** on Windows 11 — driver-free and without elevation. Parsing the
> `0x004C` payload bytes is a separate research unit (`chore:research-continuity-parser`, #13).

## Sources

1. [Bluetooth LE Advertisements (conceptual) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/ble-beacon)
   — the canonical how-to: watcher setup, **Active scanning to also receive scan-response
   advertisements** (`watcher.ScanningMode = BluetoothLEScanningMode.Active;`), the
   `BluetoothLEManufacturerData` + `CompanyId` **manufacturer-data filter** pattern
   (`watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(...)`, "set before you start
   the watcher"), and reading RSSI via `eventArgs.RawSignalStrengthInDBm`.
2. [BluetoothLEAdvertisementWatcher class — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher?view=winrt-26100)
   — member surface (`ScanningMode`, `AdvertisementFilter`, `SignalStrengthFilter`,
   `AllowExtendedAdvertisements`, `Start`/`Stop`, `Received`/`Stopped`); the **App-capabilities
   table lists only `bluetooth`** (never `location`); introduced Windows 10 10240, stable
   through the current SDK; `AllowExtendedAdvertisements` added in 2004/19041 (default False).
3. [BluetoothLEAdvertisementWatcher.ScanningMode property — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementwatcher.scanningmode?view=winrt-26100)
   — `ScanningMode` is a `BluetoothLEScanningMode` (`Active` / `Passive` / `None`); capability `bluetooth`.
4. [BluetoothLEAdvertisementFilter class — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementfilter?view=winrt-26100)
   — the decisive filter-semantics remark: a filter is applied to an **advertisement event
   packet, not to the device/source as a whole**; a device can broadcast several packets, and
   only packets containing the filtered section are surfaced. Also exposes `BytePatterns`.
5. [BluetoothLEAdvertisementReceivedEventArgs.RawSignalStrengthInDBm property — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementreceivedeventargs.rawsignalstrengthindbm?view=winrt-22621)
   — RSSI is an `Int16` in dBm; may be raw or filtered depending on `SignalStrengthFilter`;
   the synthesized out-of-range event reports **-127**.
6. [Windows apps — packaging, deployment, and process (App capabilities / AppContainer vs Medium IL) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/get-started/intro-pack-dep-proc)
   — decisive for the capability question: "App capabilities (for example, internetClient,
   location, microphone, and bluetooth) are relevant **mostly to packaged apps that run in an
   AppContainer**"; capabilities are configured in the app package manifest "and that's why
   they **apply only to packaged apps**"; a non-AppContainer app is a **Medium IL** app;
   unpackaged apps have no app package manifest.
7. [Bluetooth (conceptual hub) — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/bluetooth)
   — states "You must declare the 'bluetooth' capability in *Package.appxmanifest*" — i.e. the
   requirement is expressed purely as a **packaged (appxmanifest) `DeviceCapability`**, the
   framing that Source 6 scopes to AppContainer/packaged apps.
8. [hbldh/bleak issue #1440 — active scan data vs passive manufacturer_data on the WinRT backend](https://github.com/hbldh/bleak/issues/1440)
   — independent (non-Microsoft) confirmation from a widely used cross-platform BLE library that
   on the Windows/WinRT backend the **primary advertisement and the scan response arrive as
   separate events** (Windows does not merge SCAN_RSP with the primary packet) and that active
   scanning is what surfaces the scan-response manufacturer data. Corroborated by real-world
   unpackaged desktop use ([bleak issue #57](https://github.com/hbldh/bleak/issues/57)).
9. [Android — Bluetooth permissions (cross-check for the Location question)](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)
   — confirms the "Location permission for BLE scanning" rule is an **Android** platform rule
   (required on Android 6–11; relaxed on 12+ via `BLUETOOTH_SCAN`/`neverForLocation`); it is
   **not** a Windows requirement — Windows gates BLE advertisement reception on the `bluetooth`
   device capability (packaged apps only), never on Location.

Prior-art corroboration (patterns only; GPL-family, nothing copied): AirPodsDesktop and CAPoD are
**unpackaged Win32 desktop apps** that receive Apple `0x004C` proximity advertisements via this
exact WinRT watcher with **no admin, no driver, and no capability manifest** — a real-world proof
of the driver-free / no-elevation path (see `docs/prior-art.md`).

## Consensus

### Scanning mode — Active (majority) captures the full manufacturer payload

- `BluetoothLEAdvertisementWatcher.ScanningMode` is a `BluetoothLEScanningMode` enum
  (`Active`, `Passive`, `None`). The default is **Passive**. Passive listens only to the
  primary advertising packets; **Active** additionally issues a scan request so the peripheral's
  **scan response (SCAN_RSP)** is also delivered. (Sources 1, 3.)
- To also receive scan-response advertisements, set — **before `Start()`** —
  `watcher.ScanningMode = BluetoothLEScanningMode.Active;`. Microsoft notes this "will cause
  greater power drain and is **not available while in background modes**." (Source 1.)
- **Windows does not merge the primary advertisement and the scan response into one event.**
  The filter is "applied to an advertisement event packet, and not to the device/source of the
  advertisement as a whole" (Source 4), and the WinRT backend surfaces the primary packet and
  the SCAN_RSP as **separate `Received` events** (Source 8). The consumer must therefore handle
  each packet independently; a device's total advertised data may be split across events.
- **Decision: use Active scanning** for `WinRtBleScanner`. Apple's Continuity proximity message
  rides in the manufacturer-specific data (company id `0x004C`); depending on model/firmware,
  parts of that data can be carried in the scan response, so Active is the only mode that
  guarantees the **full** payload is captured. This matches the spec's Prior Decision and the
  prior-art implementations. PodBridge runs as a foreground tray process, so the "not available
  in background modes" caveat and the extra power drain are acceptable.

### Company-id `0x004C` manufacturer-data filter

- Apple's Bluetooth SIG company identifier is **`0x004C`** (decimal 76); this is the value the
  spec calls out for AirPods on the advertisement path.
- Build the filter and attach it **before `Start()`** (Source 1):

  ```csharp
  var appleManufacturerData = new BluetoothLEManufacturerData { CompanyId = 0x004C };
  watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(appleManufacturerData);
  ```

  `AdvertisementFilter` is a `BluetoothLEAdvertisementFilter`; its `Advertisement.ManufacturerData`
  is a collection of `BluetoothLEManufacturerData` (each `CompanyId` = `ushort`, `Data` = `IBuffer`).
- **Filter semantics (Source 4):** matching is **per advertisement packet and section-based** —
  only packets that contain a manufacturer-data section matching the filter are surfaced; packets
  from the same device that lack it are dropped. Setting `CompanyId` with **no `Data` bytes**
  filters on the company id alone (no payload prefix to match), which is what we want to admit all
  Apple `0x004C` packets regardless of the specific proximity payload.
- **Robustness recommendation:** the built-in manufacturer-data filter's exact matching (and
  whether OS/driver-level hardware offload is used) can vary across radios and Windows builds, so
  the implementation should **also verify the company id in the `Received` handler** by scanning
  `eventArgs.Advertisement.ManufacturerData` for `CompanyId == 0x004C` before decoding. This
  belt-and-suspenders approach (filter to cut callback volume + handler-side check for
  correctness) is what independent implementations do and keeps behaviour deterministic for the
  device-independent fake in tests.
- Read the payload from `eventArgs.Advertisement.ManufacturerData` (an `IList<BluetoothLEManufacturerData>`);
  each entry's `.Data` (`IBuffer`) is read with a `DataReader` into the raw bytes the Core
  `ContinuityParser` decodes. `BytePatterns` (Source 4) is an alternative offset/pattern filter,
  not needed here.

### Reading RSSI

- In the `Received` handler, read `eventArgs.RawSignalStrengthInDBm` — an `Int16` (dBm).
  (Sources 1, 5.)

  ```csharp
  private void OnReceived(BluetoothLEAdvertisementWatcher sender,
                          BluetoothLEAdvertisementReceivedEventArgs args)
  {
      short rssi = args.RawSignalStrengthInDBm;
      ushort companyId = /* from args.Advertisement.ManufacturerData */;
  }
  ```

- This value is the raw RSSI unless a `SignalStrengthFilter` is configured, in which case it may
  be a filtered value; the synthesized **out-of-range** event carries `-127`. If a
  `SignalStrengthFilter` (`InRangeThresholdInDBm` / `OutOfRangeThresholdInDBm` /
  `OutOfRangeTimeout`) is used, account for the `-127` sentinel. (Source 5.) RSSI feeds the
  spec's strongest-RSSI device-disambiguation heuristic.

### Windows capability / permission for an unpackaged `asInvoker` desktop app

- The reference pages express the requirement as an **appxmanifest `DeviceCapability`**:
  `<DeviceCapability Name="bluetooth" />`, and the API tables list the app capability `bluetooth`
  (Sources 1, 2, 3, 7). Crucially, that is the **packaged-app** model.
- **App capabilities apply only to packaged apps that run in an AppContainer** (Source 6, verbatim
  scope): they are declared in the app package manifest, "and that's why they apply only to
  packaged apps." A non-AppContainer app is a **Medium IL** app, and **unpackaged apps have no
  app package manifest** at all.
- **Therefore, PodBridge's default tier — an unpackaged, `asInvoker` (Medium IL) .NET desktop
  app — needs NO capability declaration to use `BluetoothLEAdvertisementWatcher`.** It runs with
  the user's token and accesses the BLE radio directly; there is no appxmanifest and no capability
  gate to satisfy. (The lone Medium-IL capability exception Source 6 names is the `runFullTrust`
  *restricted* capability, which is a packaged-app concept and irrelevant to an unpackaged app.)
- **Location is NOT required.** The only device capability the BLE-Advertisement APIs ever list is
  `bluetooth`; `location` never appears (Sources 1, 2, 3). The "Location permission to scan BLE"
  rule is an **Android** platform rule, not a Windows one (Source 9). On Windows, no Location
  privacy setting gates BLE advertisement reception for a desktop app, and no Windows radios/Location
  capability is needed for advertisement watching (this differs from some radio-*management* APIs).
- **When packaging arrives (Phase 5, MSIX):** if PodBridge is later shipped as a **packaged**
  (MSIX/AppContainer) app, it must then declare `<DeviceCapability Name="bluetooth" />` in the
  package manifest — a non-admin capability, granted at install with no elevation. This does not
  affect the Phase-2 unpackaged default tier.

### No elevation, no driver

- Every API used here (`BluetoothLEAdvertisementWatcher`, `BluetoothLEAdvertisementFilter`,
  `BluetoothLEManufacturerData`, `BluetoothLEAdvertisementReceivedEventArgs`) is a user-mode
  WinRT type. **None requires administrator rights, elevation, or a kernel driver.** The prior-art
  unpackaged desktop apps (AirPodsDesktop, CAPoD) confirm the driver-free / no-admin path in
  practice. `asInvoker` is sufficient — consistent with the constitution's Tier-1 rule.

## Recommended approach (for issue #16, `WinRtBleScanner`)

1. Construct `BluetoothLEAdvertisementWatcher`; set `ScanningMode = BluetoothLEScanningMode.Active`
   **before** `Start()`.
2. Add a `0x004C` manufacturer-data filter
   (`AdvertisementFilter.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData { CompanyId = 0x004C })`)
   to reduce callback volume — and **also** verify `CompanyId == 0x004C` in the `Received` handler
   before emitting, so behaviour is deterministic regardless of hardware-filter quirks.
3. On each `Received`, emit raw scanner data behind Core's `IBleScanner`: peer address,
   `RawSignalStrengthInDBm` (RSSI), and the matching manufacturer section's company id + payload
   bytes (via `DataReader` over the `IBuffer`). Do **no** decoding in the adapter — the Core
   `ContinuityParser` owns byte semantics (architecture: Core is OS-free).
4. Leave `AllowExtendedAdvertisements` at its default (`False`): Apple Continuity uses legacy
   advertising, not the extended PDU format, so it is not needed. (Source 2.)
5. Handle `Stopped` and radio-off/absent gracefully: `Start()` may succeed with no radio and simply
   deliver no events; combine with Phase-1's radio-presence check (`Radio.GetRadiosAsync` /
   `BluetoothAdapter.GetDefaultAsync`) for the "Bluetooth unavailable" tray state and the staleness
   timeout for out-of-range — never crash, never show a stale value as live.
6. Ship as **x64/ARM64** (matching the OS architecture), consistent with the Phase-1 radio-enumeration
   caveat.
7. No appxmanifest / capability is required for the unpackaged `asInvoker` default tier; do not
   request elevation.

## Disputes (minority → majority decision)

- **Active vs Passive scanning.** *Minority:* passive scanning is lower-power and, when the AirPods
  `0x004C` proximity message is carried in the primary `ADV_IND`, is often sufficient to read
  battery/in-ear. *Majority (Sources 1, 3, 4, 8):* use **Active** — it is the only mode that also
  delivers the scan response, guaranteeing the full manufacturer payload across models/firmware;
  the extra power drain is acceptable for a foreground tray app. **Decision: Active.**
- **Does an unpackaged `asInvoker` app need the `bluetooth` capability?** *Minority reading* (a
  literal read of Sources 1/2/3/7): "You must declare the 'bluetooth' capability" — implying every
  app needs it. *Majority / correct scope (Source 6):* capabilities are an **appxmanifest /
  AppContainer** concept that "apply only to packaged apps"; an unpackaged Medium-IL desktop app
  has no manifest and needs **no** capability. Prior-art unpackaged apps confirm it works without
  one. **Decision: no capability required for the Phase-2 unpackaged tier; declare `bluetooth` only
  if/when PodBridge is packaged as MSIX in Phase 5.**
- **Is Location required for BLE scanning?** *Minority (by analogy to Android):* Android requires
  Location for BLE scanning, so Windows might too. *Majority (Sources 1/2/3 list only `bluetooth`;
  Source 9 scopes the Location rule to Android):* **Location is not required on Windows** for
  advertisement watching. **Decision: no Location capability or permission is needed.**
- **Rely on the built-in manufacturer-data filter alone, or also check in the handler?** *Minority:*
  the `AdvertisementFilter` should be enough. *Majority:* the filter is applied per packet and its
  exact/hardware-offload behaviour can vary, so **also verify `CompanyId == 0x004C` in the handler**
  for deterministic behaviour and clean fakes in tests. **Decision: filter + handler-side check.**
