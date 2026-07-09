# Research: IPolicyConfig / IPolicyConfig2 P/Invoke contract + audio-endpoint→device match

> Permanent record for the `chore:research-ipolicyconfig` issue (#25).
> Authority for the Phase-4 `WindowsAudioPolicy` implementation issue (#28).
> Clean-room (Apache-2.0): this file records **facts only** — interface IIDs,
> the CLSID, the vtable method order, the `SetDefaultEndpoint` signature, the
> `ERole` values, and the MMDevice property-key names — described in my own
> words from Microsoft Learn plus cross-checked public implementations. No GPL
> source or verbatim protocol-doc prose is reproduced.
>
> Scope: confirm the undocumented, reverse-engineered `IPolicyConfig` /
> `IPolicyConfig2` COM contract so a **driver-free, admin-free** adapter can set
> the default vs default-**communications** render/capture endpoint **per
> `ERole`** on **Windows 11 21H2+**, and confirm how to match an enumerated
> **MMDevice audio endpoint** to a specific connected Bluetooth device (the
> AirPods). This endpoint→device match is a **distinct mapping** from the Phase-1
> name heuristic and Phase-2 BLE company-id, which identify a *Bluetooth device*,
> not an *audio endpoint*.

## Sources

1. [tartakynov/audioswitch — `IPolicyConfig.h`](https://github.com/tartakynov/audioswitch/blob/master/IPolicyConfig.h)
   — C++ header. Gives `IPolicyConfig` IID `f8679f50-…`, `CPolicyConfigClient`
   CLSID `870af99c-…`, the Vista IID/CLSID, and the full 12-method vtable with
   `SetDefaultEndpoint` at slot 11.
2. [File-New-Project/EarTrumpet — `Interop/MMDeviceAPI/IPolicyConfig.cs`](https://github.com/File-New-Project/EarTrumpet/blob/dev/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs)
   — **primary modern C# consumer** (a mainstream, actively-maintained Windows
   volume mixer). Declares the interface under IID `F8679F50-…` and annotates it
   "Win7 / Win8 / W10_RS1-Present", i.e. the same interface across current
   Windows. It stubs the first **eight** methods as `Unused1…Unused8` before
   `GetPropertyValue`, `SetPropertyValue`, `SetDefaultEndpoint`,
   `SetEndpointVisibility` — independently pinning `SetDefaultEndpoint` to
   vtable slot 11 and confirming the 8-methods-before-`GetPropertyValue` layout.
3. [ThiefMaster/coreaudio-dotnet — `CoreAudio/Interfaces/IPolicyConfig.cs`](https://github.com/ThiefMaster/coreaudio-dotnet/blob/master/CoreAudio/Interfaces/IPolicyConfig.cs)
   — C# projection. Same IID and `SetDefaultEndpoint` signature; **minority
   layout** — omits `ResetDeviceFormat` (11 methods). See Disputes.
4. [DanStevens/AudioEndPointController — `EndPointController/PolicyConfig.h`](https://github.com/DanStevens/AudioEndPointController/blob/master/EndPointController/PolicyConfig.h)
   — C++ header. Confirms both IIDs + both CLSIDs, the 12-method `IPolicyConfig`
   vtable, and the distinct **11-method `IPolicyConfigVista`** layout (Vista has
   no `ResetDeviceFormat`, so its `SetDefaultEndpoint` is at slot 10).
5. [matzman666/OpenVR-AdvancedSettings — `IPolicyConfig.h`](https://github.com/matzman666/OpenVR-AdvancedSettings/blob/master/src/tabcontrollers/audiomanager/IPolicyConfig.h)
   — C++ header. Independently lists three variant IIDs: `f8679f50-…`
   (labelled `IPolicyConfig0`), `6be54be8-…` (`IPolicyConfig1`), and
   `ca286fc3-…` (`IPolicyConfig2`); same CLSID; same 12-method vtable.
6. [frgnca/AudioDeviceCmdlets — `SOURCE/IPolicyConfig.cs`](https://github.com/frgnca/AudioDeviceCmdlets/blob/master/SOURCE/IPolicyConfig.cs)
   — the PowerShell `AudioDeviceCmdlets` module (widely used). Same IID,
   `[PreserveSig] int SetDefaultEndpoint(string, ERole)`, 12-method layout with
   `ResetDeviceFormat` present.
7. [sidit77/com-policy-config — Rust bindings (`src/lib.rs`)](https://github.com/sidit77/com-policy-config)
   — modern Rust `windows-rs` bindings. Same IID, same CLSID, identical
   12-method order, `SetDefaultEndpoint(PCWSTR, ERole)`.
8. [Microsoft Learn — ERole enumeration (`mmdeviceapi.h`)](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/ne-mmdeviceapi-erole)
   — **authoritative** for the `ERole` values: `eConsole = 0`, `eMultimedia = 1`,
   `eCommunications = 2`, `ERole_enum_count = 3`, and what each role means.
9. [Microsoft Learn — Device Properties (Core Audio APIs)](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties)
   — **authoritative** for the endpoint→device match: every audio endpoint's
   property store exposes `PKEY_Device_ContainerId` ("the container identifier of
   the PnP device that implements the audio endpoint") and `PKEY_Device_FriendlyName`
   (the fallback). Keys defined in `Functiondiscoverykeys_devpkey.h`.
10. [m2jean/ToothTray — README](https://github.com/m2jean/ToothTray)
    — corroborates the Bluetooth case: a Bluetooth audio device that supports
    multiple profiles appears as **multiple** audio endpoints, and endpoints of
    one physical device can be grouped by matching `PKEY_Device_ContainerId`.
11. [Microsoft Learn — DEVPKEY_Device_ContainerId / Container IDs](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/container-ids)
    — the container id is a GUID that PnP assigns to group all functions/endpoints
    of one physical device; a single Bluetooth peripheral has one container id.

## Consensus

### Interface IIDs and the CLSID

- **`IPolicyConfig` IID:** `f8679f50-850a-41cf-9c72-430f290290c8`
  (unanimous — sources 1–7). This is the interface every mainstream switcher
  actually Queries and uses on Windows 7 through Windows 11.
- **`IPolicyConfig2` IID:** `ca286fc3-91fd-42c3-8e9b-caafa66242e3`
  (sources 5, plus the initial harvest). A superset of `IPolicyConfig` that adds
  one method at the end (`SetEndpointVisibility` differences aside — see below);
  **not required** for PodBridge's role-switching, which only needs
  `SetDefaultEndpoint`.
- **Intermediate variant IID (`IPolicyConfig1`):** `6be54be8-a068-4875-a49d-0c2966473b11`
  (source 5) — an OS-era intermediate; PodBridge does not target it.
- **`CPolicyConfigClient` CLSID (the coclass to `CoCreateInstance`):**
  `870af99c-171d-4f9e-af0d-e63df40c2bc9` (unanimous — sources 1, 4, 5, 7). This
  single CLSID exposes all of the `IPolicyConfig*` variants; you create it, then
  `QueryInterface` for the interface you want.
- **Vista-only variant (legacy, informational):** `IPolicyConfigVista` IID
  `568b9108-44bf-40b4-9006-86afe5b5a620`, `CPolicyConfigVistaClient` CLSID
  `294935ce-f637-4e7c-a41b-ab255460b862` (sources 1, 4). Below PodBridge's
  minimum (Win11 21H2+); irrelevant except as the reason a `SetDefaultEndpoint`
  fallback path exists in some prior art.

### Vtable method order (the load-bearing fact for P/Invoke)

`IPolicyConfig` derives directly from `IUnknown`. After the three `IUnknown`
slots (`QueryInterface`, `AddRef`, `Release`) the twelve methods appear in this
exact order (sources 1, 4, 5, 6, 7; corroborated by source 2's placeholder count):

1. `GetMixFormat`
2. `GetDeviceFormat`
3. `ResetDeviceFormat`
4. `SetDeviceFormat`
5. `GetProcessingPeriod`
6. `SetProcessingPeriod`
7. `GetShareMode`
8. `SetShareMode`
9. `GetPropertyValue`
10. `SetPropertyValue`
11. **`SetDefaultEndpoint`**  ← the only method PodBridge calls
12. `SetEndpointVisibility`

The order is what matters for a hand-written vtable/P/Invoke interface: even the
methods PodBridge never calls (1–10, 12) **must be declared in place** (correct
signatures or opaque placeholders that consume the same slot) so that slot 11
resolves to `SetDefaultEndpoint`. Getting the count wrong shifts every later
slot and silently calls the wrong function.

### `SetDefaultEndpoint` signature

Native (sources 1, 4, 5, 7):

```
HRESULT SetDefaultEndpoint(
    [in] PCWSTR wszDeviceId,   // the MMDevice endpoint ID string (IMMDevice::GetId)
    [in] ERole  eRole          // which role to (re)assign
);
```

C# projection as used by the prior art (sources 3, 6):

```csharp
[PreserveSig]
int SetDefaultEndpoint(
    [MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId,
    ERole eRole);
```

- `wszDeviceId` is the **endpoint ID string** returned by `IMMDevice::GetId`
  (the opaque `{0.0.0.00000000}.{guid}` form) — *not* a friendly name.
- Prefer `[PreserveSig] int …` so the adapter can inspect the `HRESULT`
  (source 6); the `void` form (sources 2, 3) throws a `COMException` on failure.
- **Per-role, one call each.** Setting a device as the system default for *all*
  purposes means calling `SetDefaultEndpoint` once **per role** — typically
  `eConsole`, `eMultimedia`, and `eCommunications`. For Phase-4 role-splitting
  this is exactly the lever: assign the AirPods to `eConsole`/`eMultimedia` while
  assigning the fallback device to `eCommunications` (HiFi-lock), or promote the
  AirPods to `eCommunications` (Auto-switch / Call-mode) — one `SetDefaultEndpoint`
  call per (device, role) pair.

### `ERole` enumeration (authoritative — source 8)

```
eConsole        = 0   // games, system notification sounds, voice commands
eMultimedia     = 1   // music, movies, narration, live music recording
eCommunications = 2   // voice communications (talking to another person)
ERole_enum_count = 3
```

The Windows "Default Device" is `eConsole`+`eMultimedia`; the "Default
Communication Device" is `eCommunications`. The whole Phase-4 policy is built on
the fact that these two are settable independently, which is precisely what
lets media stay on the A2DP endpoint while the comms role points elsewhere.

### Windows 11 21H2+ build variance

- The interface is **undocumented** by Microsoft, but the IID `f8679f50-…`,
  the CLSID `870af99c-…`, and the 12-method layout have been **stable from
  Windows 10 RS1 through current Windows 11** — source 2 explicitly labels its
  single definition "W10_RS1-Present", and sources 6 and 7 (both maintained in
  the Win11 era) use the identical layout. No source reports a per-build GUID or
  vtable change within the Win11 21H2 → 24H2 range.
- `ERole` and `PKEY_Device_ContainerId` are **documented and stable** (sources
  8, 9), so only the `IPolicyConfig` surface carries any risk.
- Because it is reverse-engineered, the risk is nonetheless real: the adapter
  must isolate all of this behind `IAudioPolicy` (per the spec/constitution),
  fail gracefully if `CoCreateInstance`/`QueryInterface`/`SetDefaultEndpoint`
  returns an error rather than crashing, and be verified on real hardware at the
  QA gate. A defensive `IPolicyConfig` → `IPolicyConfigVista` fallback (the
  EarTrumpet pattern, source 2) is available if a future build ever regresses,
  though it is not expected to be needed on 21H2+.

### MMDevice audio-endpoint → Bluetooth-device match

This answers "which enumerated render/capture endpoint **is** the AirPods" — a
mapping from an *audio endpoint* to a *device*, distinct from Phase-1/2 Bluetooth
identification.

- **Primary key — `PKEY_Device_ContainerId`** (sources 9, 10, 11). Every audio
  endpoint's property store (via `IMMDevice::OpenPropertyStore(STGM_READ)` →
  `IPropertyStore::GetValue`) carries `PKEY_Device_ContainerId`, the GUID of the
  PnP **device container** that implements the endpoint. All endpoints of one
  physical device share the same container id (source 9). A Bluetooth headset
  that exposes both A2DP and HFP therefore surfaces its **render and capture
  endpoints under one container id** (source 10). The container id is defined in
  `Functiondiscoverykeys_devpkey.h` as a `PROPERTYKEY` (VT_CLSID value).
- **Linking it to *the AirPods specifically*:** the connected AirPods Bluetooth
  device object also has a container id (PnP assigns one container id per physical
  peripheral — source 11; exposed to WinRT as `System.Devices.ContainerId` on the
  `DeviceInformation`/`BluetoothDevice`, and to Win32/SetupAPI as
  `DEVPKEY_Device_ContainerId`). The adapter obtains the AirPods' container id
  from the connected-device signal it already has (Phase-1/2), then tags each
  audio endpoint whose `PKEY_Device_ContainerId` **equals** that GUID as
  `isAirPods = true`. This deterministically groups the AirPods' render + capture
  endpoints and separates them from every other device's endpoints.
- **Fallback — `PKEY_Device_FriendlyName`** (source 9). When the container id is
  unavailable or cannot be matched (driver-stack variance, virtual/dock audio
  devices, or the Bluetooth-side container id could not be resolved), fall back
  to the endpoint's friendly name (e.g. "Headphones (AirPods Pro)") and match a
  device-name substring — the same class of heuristic as Phase 1, but applied to
  the *audio endpoint's* name. This is explicitly the lower-confidence path.
- **Why not reuse Phase-1/2 identity directly:** those identify the *Bluetooth
  device* (BLE advertisement company-id 0x004C / name heuristic). They never see
  an audio endpoint. The container id is the documented bridge that carries a
  device identity across to the CoreAudio endpoint layer, so Core can keep
  routing purely on the adapter-supplied `isAirPods` flag while staying OS-free.

## Recommended approach (for the Phase-4 implementation issue #28)

Inside `WindowsAudioPolicy` (the only place any of this lives), behind Core's
`IAudioPolicy`:

1. `CoCreateInstance(CLSID_PolicyConfig = 870af99c-…, CLSCTX_ALL)` then
   `QueryInterface` for `IPolicyConfig` (IID `f8679f50-…`). Declare the interface
   with **all 12 methods in order** (opaque placeholders for slots 1–10 and 12
   are fine) so `SetDefaultEndpoint` sits at slot 11. Optionally keep a
   `IPolicyConfigVista` (`568b9108-…`) fallback for robustness.
2. Enumerate endpoints with `IMMDeviceEnumerator` (NAudio may do the
   enumeration; NAudio **cannot** set defaults — hence `IPolicyConfig`). For each
   endpoint read `PKEY_Device_ContainerId` (primary) and `PKEY_Device_FriendlyName`
   (fallback); set `isAirPods` by matching the connected AirPods' container id,
   else the friendly-name substring.
3. To assign a role: `SetDefaultEndpoint(endpoint.GetId(), eRole)` — one call per
   role. HiFi-lock: AirPods → `eConsole` + `eMultimedia`, fallback device →
   `eCommunications` (render **and** capture). Auto-switch / Call-mode: AirPods →
   `eCommunications` too. Restore by reasserting the HiFi-lock assignment.
4. Treat any non-`S_OK` `HRESULT` (use `[PreserveSig]`) as a soft failure →
   surface through `IAudioPolicy` so Core can degrade honestly; never crash.
5. Keep every type above in `PodBridge.Windows`; Core sees only `IAudioPolicy`
   and the `isAirPods` flag — no COM, no P/Invoke, no `ERole` leakage into Core.

## Disputes (minority → majority decision)

- **`ResetDeviceFormat` present (12 methods) vs absent (11 methods).**
  Minority: source 3 (coreaudio-dotnet C#) declares `IPolicyConfig` with only 11
  methods, omitting `ResetDeviceFormat` at slot 3 — which would put
  `SetDefaultEndpoint` at slot 10. The legacy `IPolicyConfigVista` (source 4)
  genuinely has that 11-method layout, which is likely the origin of the mistake.
  **Majority (sources 1, 4, 5, 6, 7 + the decisive source 2):** the current
  `f8679f50` interface has **12 methods including `ResetDeviceFormat` at slot 3,
  `SetDefaultEndpoint` at slot 11.** Source 2 is decisive because EarTrumpet
  stubs exactly **eight** `Unused` methods before `GetPropertyValue`; an
  11-method layout would need only seven. → **Decision: 12-method layout,
  `ResetDeviceFormat` included, `SetDefaultEndpoint` at slot 11.** (An 11-method
  declaration against the real 12-method vtable would misfire the call.)

- **`IPolicyConfig` vs `IPolicyConfig2` — which to use.** The spec names both.
  Minority framing (source 5) numbers them `IPolicyConfig0/1/2`. **Majority
  (sources 1–7):** for setting default endpoints, `IPolicyConfig` (`f8679f50`)
  is universal and sufficient; `IPolicyConfig2` (`ca286fc3`) is a compatible
  superset adding a trailing method not needed here. → **Decision: target
  `IPolicyConfig` (`f8679f50`); `IPolicyConfig2` may be queried opportunistically
  but is not required.** Both come from the same CLSID `870af99c-…`.

- **`SetDefaultEndpoint` return marshalling — `void` vs `[PreserveSig] int`.**
  Minority: sources 2, 3 declare it `void` (exceptions on failure). **Majority
  practice (source 6) and the constitution's honesty/degradation goals:** use
  `[PreserveSig] int` so the `HRESULT` is inspectable and the adapter can degrade
  gracefully. → **Decision: `[PreserveSig] int`.** (Both are ABI-identical — an
  `HRESULT`-returning method — so this is a projection choice, not a vtable
  dispute.)

- **Container-id vs friendly-name as the endpoint→device key.** No real conflict
  in the sources — container id is the documented, robust key (sources 9, 10, 11)
  and friendly name is the acknowledged lower-confidence fallback (source 9). →
  **Decision: container id primary, friendly-name substring fallback**, exactly as
  the spec's Prior-decisions row states.
