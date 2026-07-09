# Research: driver-free detection of the negotiated A2DP codec (AAC vs SBC)

> Research artefact for `chore:research-codec-detection` (issue #20), feeding the
> `WindowsAudioStateReader` implementation issue under
> `docs/specs/spec-audio-transparency.md` (Phase 3). Clean-room: facts and their
> sources only, written in our own words. No GPL source or verbatim doc prose.

## Question

For a connected AirPods device on Windows 11 (21H2+), can PodBridge read the
**actually-negotiated A2DP codec** (AAC vs SBC) **driver-free and admin-free**,
and when must the result be reported as the honest `Unknown` fallback?

## Consensus

### There is no public user-mode API that returns the negotiated codec

Microsoft's own "Bluetooth Classic Audio" driver documentation describes the
codec priority list (Windows 11 21H2+ picks the first codec both host and device
support, in the order aptX Adaptive → AAC → aptX Classic → SBC) but exposes **no
API, WinRT projection, or documented property** that reports which codec was
actually selected for a live connection. Community write-ups (The Windows Club,
guru3D, ElevenForum, a Microsoft Q&A thread) agree: Windows ships **no built-in
UI or supported API** to read the active Bluetooth codec.

The two candidate "read local state" avenues both fail to reveal the codec:

- **Core Audio endpoint property store (`IMMDevice::OpenPropertyStore`,
  `PKEY_AudioEngine_OEMFormat`).** This returns the PCM `WAVEFORMATEX` that
  Windows presents to applications for the endpoint (e.g. 48 kHz stereo). That
  format is the same regardless of whether the over-the-air link is AAC or SBC,
  so it **cannot distinguish AAC from SBC**. Driver-free and admin-free, but it
  answers the wrong question.
- **Registry `HKLM\SYSTEM\CurrentControlSet\Services\BthA2dp\Parameters`
  (`BluetoothAacEnable`).** This is a machine-wide **configuration toggle**
  (whether the AAC codec is permitted at all), not a per-connection status. It
  does not tell you what the current link negotiated.

### The only driver-free mechanism that reveals the codec is ETW — and it needs elevation

Every source that actually distinguishes AAC from SBC on Windows does it the
same way: by capturing an **Event Tracing for Windows (ETW)** trace of the
Bluetooth A2DP stack and reading the codec fields off the streaming event.

- Provider: **`Microsoft.Windows.Bluetooth.BthA2dp`**
  (GUID `8776ad1e-5022-4451-a566-f47e708b9075`).
- Event: **`A2dpStreaming`** (emitted around the start/stop of an A2DP stream).
- Fields: **`A2dpStandardCodecId`**, `A2dpVendorId`, `A2dpVendorCodecId`.

The standard-codec IDs follow the Bluetooth A2DP media-codec-type assignments:

| `A2dpStandardCodecId` | Codec |
| --- | --- |
| `0x00` | SBC |
| `0x01` | MP3 (MPEG-1/2 Audio) |
| `0x02` | **AAC** (MPEG-2/4 AAC) |
| `0x04` | ATRAC |
| `0xFF` | Vendor-specific — resolve via `A2dpVendorId` + `A2dpVendorCodecId` (e.g. aptX, LDAC) |

For AirPods on Windows the outcome is therefore binary in practice: `0x02` = AAC
(the quality ceiling on Windows) or `0x00` = SBC fallback. AirPods advertise no
aptX/LDAC, so vendor IDs are not expected.

Two independent tools implement exactly this:

- **Helge Klein's guide** captures the trace with Windows Performance Recorder
  (`wpr.exe -start BluetoothStack.wprp!BluetoothStack`, profile from Microsoft's
  `busiotools` repo), stops it to an `.etl`, and reads `A2dpStandardCodecId` /
  `A2dpVendorId` / `A2dpVendorCodecId` in Windows Performance Analyzer. WPR
  requires an **elevated** command prompt.
- **`imbushuo/BluetoothAudioCodecInspector`** does the same read
  **programmatically** (C#, `TraceEvent` `TraceEventSession`) with no WPA
  install, using the provider/event/fields above and the same `0x00`→SBC,
  `0x02`→AAC mapping. Critically, it **guards its entry point with
  `TraceEventSession.IsElevated()` and refuses to run without administrator
  rights** — because starting a real-time ETW session for this provider requires
  elevation (a standard user is not in "Performance Log Users" by default).

So the ETW read is **driver-free** (no kernel driver installed; the provider is
part of the in-box Bluetooth stack) but it is **not admin-free**: both the manual
(WPR) and the programmatic (`TraceEventSession`) routes need elevation to start
the session.

### AAC vs SBC distinction (driver-free)

When (and only when) an elevated ETW session on `Microsoft.Windows.Bluetooth.BthA2dp`
captures an `A2dpStreaming` event, `A2dpStandardCodecId == 0x02` → **AAC**,
`== 0x00` → **SBC**. This is the single authoritative, non-heuristic signal all
sources agree on. Inferring the codec from the priority list ("AAC is enabled and
AirPods support AAC, therefore AAC") is explicitly **not** reliable — real-world
reports show Windows negotiating AAC with devices that then fail, and falling
back per adapter/driver — so inference is rejected in favour of the honest
`Unknown` fallback.

### When the result must be `Unknown` (honest fallback)

Report `CodecKind.Unknown` — never a guess — in every case below:

1. **Not elevated (the PodBridge default).** Phase 3 ships `asInvoker`,
   admin-free by hard constraint, so it cannot start the ETW session. With no
   admin-free API exposing the codec, the honest default-tier result is
   `Unknown`.
2. **No `A2dpStreaming` event observed.** The event surfaces around active
   streaming, and the stack may emit it late or only during playback (chipset
   dependent). If nothing is captured within the read window, the codec is
   undetermined → `Unknown`.
3. **No A2DP link / device in HFP-only or call mode.** If the link is not in
   A2DP (e.g. the mic is engaged, HFP), there is no A2DP codec to report →
   `Unknown` (mic-mode is a separate research unit).
4. **Vendor / unrecognised codec id.** `0xFF` with an unknown vendor pair, or any
   id outside the known table → `Unknown` rather than a fabricated label.
5. **Adapter/driver variance.** The provider or fields may be absent on some
   Bluetooth stacks/builds; absence → `Unknown`, never a crash.

### No-admin / no-driver confirmation

- **No driver:** the `BthA2dp` provider is part of the in-box Windows Bluetooth
  stack; nothing is installed. Confirmed against Microsoft's Bluetooth Classic
  Audio docs and both tools.
- **No admin — but only for the honest `Unknown` result.** The admin-free,
  driver-free surface (Core Audio property store, registry) can confirm a
  connected Bluetooth audio endpoint exists but **cannot** reveal AAC vs SBC. A
  positive AAC/SBC read is achievable **only with elevation**, which Phase 3
  forbids. Therefore, within PodBridge's admin-free Tier-1 constraint, the
  negotiated codec is generally **not determinable**, and the reader must return
  `Unknown` by design rather than build an unreliable inference. This is exactly
  the first-class honest fallback the spec anticipated (Risks row: "Windows
  exposes no clean public API … `Unknown` is a first-class honest fallback").

## Recommendation for `WindowsAudioStateReader`

- Default (admin-free) path: attempt only driver-free/admin-free checks; since
  none expose the codec, return `CodecKind.Unknown`. Do **not** infer from the
  priority list or endpoint PCM format, and do **not** silently request
  elevation.
- Keep the ETW read (`Microsoft.Windows.Bluetooth.BthA2dp` → `A2dpStreaming` →
  `A2dpStandardCodecId`) documented as the *only* mechanism that yields a
  positive AAC/SBC value, so that if an explicit, user-opted-in elevated read is
  ever added it uses this exact provider/event/field mapping. It is out of scope
  for the admin-free Tier-1 reader.
- The guidance engine already treats `Unknown` as "couldn't determine" and
  suppresses the SBC advice notification, so an `Unknown`-heavy default degrades
  gracefully and honestly.

## Sources

1. Microsoft Learn — *Bluetooth Classic Audio (Windows drivers)*.
   <https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio>
   — codec priority list, AAC requires Windows 11 21H2+, Win11 unified A2DP/HFP
   endpoints; no API for the negotiated codec is documented.
2. Helge Klein — *How to Check Which Bluetooth A2DP Audio Codec Is Used on
   Windows*.
   <https://helgeklein.com/blog/how-to-check-which-bluetooth-a2dp-audio-codec-is-used-on-windows/>
   — ETW/WPR/WPA method, `BluetoothStack.wprp`, `A2dpStandardCodecId` (0x00 SBC,
   0x02 AAC) etc.; requires an elevated capture.
3. `imbushuo/BluetoothAudioCodecInspector` (GitHub, source inspected).
   <https://github.com/imbushuo/BluetoothAudioCodecInspector> — programmatic ETW
   read of provider `Microsoft.Windows.Bluetooth.BthA2dp`
   (`8776ad1e-5022-4451-a566-f47e708b9075`), event `A2dpStreaming`, fields
   `A2dpStandardCodecId`/`A2dpVendorId`/`A2dpVendorCodecId`; explicit
   `IsElevated()` guard → elevation required.
4. Microsoft Learn — *Device Properties (Core Audio APIs)* /
   `PKEY_AudioEngine_OEMFormat`.
   <https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties>
   — the endpoint property store returns a PCM `WAVEFORMATEX`, not the over-the-
   air Bluetooth codec (negative evidence: cannot distinguish AAC vs SBC).
5. The Windows Club — *How to check Bluetooth Codec in Windows 11/10*.
   <https://www.thewindowsclub.com/how-to-check-bluetooth-codec-in-windows> —
   confirms no built-in Windows method; third-party/trace tooling only.
6. Microsoft Learn — *Windows Performance Recorder*.
   <https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-recorder>
   — WPR ETW capture requires elevation (corroborates the admin requirement).

## Disputes (minority → majority decision)

- **Can the Core Audio endpoint property store reveal the codec?** Minority:
  `PKEY_AudioEngine_OEMFormat`/endpoint format could expose it. **Decision
  (majority):** No — it returns the PCM stream format (same 48 kHz stereo for
  AAC or SBC); it does not expose the A2DP codec. Rejected as a codec source.
- **Does the `BluetoothAacEnable` registry value report the active codec?**
  Minority: reading it tells you the codec. **Decision:** No — it is a machine-
  wide enable/disable config, not per-connection negotiated status. Rejected.
- **Is there an admin-free ETW path?** Minority hope: capture `BthA2dp` as a
  standard user. **Decision:** No documented admin-free path exists — WPR needs
  elevation and the programmatic tool refuses without `IsElevated()`; a standard
  user is not in "Performance Log Users" by default. ETW read is driver-free but
  **not** admin-free.
- **Can the codec be inferred from the priority list without a trace?** Minority:
  assume AAC when AAC is enabled and the device supports it. **Decision:**
  Rejected — negotiation is adapter/driver-dependent and real reports show
  AAC-negotiated-then-failed cases; inference would lie. Prefer honest `Unknown`.
- **Third-party tools (e.g. "Bluetooth Tweaker").** Minority: bundle/recommend
  one. **Decision:** Rejected per constitution Don'ts (no paid/proprietary
  components) and unclear admin/driver profile; not part of our approach.
