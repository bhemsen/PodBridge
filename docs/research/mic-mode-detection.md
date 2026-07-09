# Research: driver-free read of A2DP (high quality) vs HFP (call) mode

> Permanent record for the `chore:research-mic-mode-detection` issue (#21).
> Authority for the Phase-3 implementation issue (#23). Clean-room: this file
> cites Microsoft Learn / official Win32 & driver reference and public community
> corroboration only — no GPL source or verbatim protocol-doc prose is reproduced.
>
> Scope: reading, **driver-free and admin-free**, whether the connected AirPods
> audio link on **Windows 11** is currently in **high-quality A2DP** (stereo
> media) or has collapsed to **HFP/call mode** (mono, mic engaged), plus the
> honest **Unknown** fallback. This is Phase 3 — **read/display only; PodBridge
> switches nothing** (all endpoint switching is Phase 4). Codec detection
> (AAC vs SBC) is a **separate** research unit (`chore:research-codec-detection`).

## Sources

1. [Bluetooth Classic Audio — Windows drivers (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio)
   — **primary.** Windows 11 unifies A2DP + HFP into **one output and one input
   endpoint**; enumerates the exact scenarios in which Windows selects HFP instead
   of A2DP (app opens the mic input endpoint; app opens a render stream with
   category *Communications*); A2DP = stereo high-rate, HFP = mono 8 kHz / 16 kHz;
   profile changes happen automatically on microphone-usage state.
2. [IAudioSessionManager2 (audiopolicy.h) — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nn-audiopolicy-iaudiosessionmanager2)
   — the read-only session surface: activate on any `IMMDevice`, then
   `GetSessionEnumerator` to **enumerate sessions on that endpoint**. Interface is
   Windows 7+, desktop apps, no elevation. Its only "action" methods
   register/unregister notifications — it has **no endpoint-switching method**.
3. [IAudioSessionControl::GetState (audiopolicy.h) — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol-getstate)
   and [AudioSessionState enum (audiosessiontypes.h)](https://learn.microsoft.com/en-us/windows/win32/api/audiosessiontypes/ne-audiosessiontypes-audiosessionstate)
   — a session's state is `AudioSessionStateActive` when it "has one or more
   streams that are running" (else `Inactive` / `Expired`). Read-only.
4. [Audio Endpoint Container ID — Windows drivers (Microsoft Learn)](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/audio-endpoint-container-id)
   — endpoints of the **same physical device** share a `PKEY_Device_ContainerId`;
   used to match the AirPods **render** endpoint to its **capture** endpoint.
5. [Device Formats / PKEY_AudioEngine_DeviceFormat — Win32 (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-formats)
   — `PKEY_AudioEngine_DeviceFormat` is **the shared-mode format the user selected**
   for the device (Sound control panel → Advanced → "Default Format"); it is a
   static, user-chosen value, **not** a live per-profile signal. Resolves the
   "read the sample rate to infer the mode" dispute.
6. [Phone Link Bluetooth call audio format — Microsoft Q&A](https://learn.microsoft.com/en-us/answers/questions/5880404/phone-link-bluetooth-call-audio-format-is-it-16-kh)
   — corroboration: an active Bluetooth call runs the headset in **HFP, mono,
   16 kHz (mSBC) or 8 kHz**; the Windows audio engine performs the resampling.
7. [EarTrumpet `IPolicyConfig.cs`](https://github.com/File-New-Project/EarTrumpet/blob/dev/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs)
   and [soyfrien/IPolicyConfig.h](https://github.com/soyfrien/IPolicyConfig.h)
   — `IPolicyConfig` prior art: the undocumented interface exists **to *set* the
   default endpoint** (`SetDefaultEndpoint`). Confirms the switching API is
   distinct from — and **not needed by** — the read-only path; Phase 3 must not
   touch it (that is Phase 4).
8. [Windows 10/11 Bluetooth Headset Audio: A2DP vs HFP, tradeoffs (Windows Forum)](https://windowsforum.com/threads/windows-10-bluetooth-headset-audio-a2dp-vs-hfp-fixes-tradeoffs.401263/)
   — independent community corroboration of the behavioural model: opening the mic
   / a communications stream forces HFP and suspends stereo A2DP; you cannot have
   stereo playback and the Bluetooth mic simultaneously.

## Consensus

### The unified-endpoint model (what we are reading)

- On **Windows 11**, if the AirPods support HFP, Windows creates exactly **one
  render (output) endpoint and one capture (input/microphone) endpoint** for the
  device — not the three separate endpoints of Windows 10. (Source 1.)
- Windows chooses the profile **automatically**. Per Source 1, Windows selects
  **HFP instead of A2DP** when either of these is true:
  1. an application opens the **input (microphone) endpoint**; or
  2. an application opens a **render (playback) stream with category
     *Communications***.
  In **all other cases** playback uses **A2DP** (high quality). Windows resamples
  automatically for the active profile (HFP = mono 8 kHz / 16 kHz). (Sources 1, 6.)
- Consequence for detection: there is **no public API that returns "current
  profile = A2DP | HFP" directly.** The mode must be **inferred, read-only**, from
  observable audio-stack state. Detection targets the two triggers above.

### The read mechanism (driver-free, admin-free, read-only)

- **Enumerate endpoints** with `IMMDeviceEnumerator`
  (`MMDeviceEnumerator` COM object). Find the AirPods **capture** endpoint and its
  paired **render** endpoint by matching **`PKEY_Device_ContainerId`** (same
  physical device → same container id). (Source 4.) All user-mode, no admin.
- **Detect trigger #1 (mic engaged → HFP)** — the reliable signal:
  on the AirPods **capture** endpoint, call `IMMDevice::Activate` for
  `IAudioSessionManager2`, then `GetSessionEnumerator` → iterate each
  `IAudioSessionControl` and read `GetState()`. If **any** session is
  `AudioSessionStateActive`, some application is actively capturing from the
  AirPods mic, which is exactly the condition under which Windows 11 switches the
  link to **HFP/call mode**. No active capture session ⇒ the link is free to run
  **A2DP** for playback. (Sources 1, 2, 3.)
- This is **strictly read-only**: `IMMDeviceEnumerator`, `IMMDevice` property
  reads, `IAudioSessionManager2::GetSessionEnumerator`, `IAudioSessionControl::GetState`
  are all **enumeration / query** calls. None switches an endpoint. The only
  switching API (`IPolicyConfig::SetDefaultEndpoint`) is deliberately **not**
  used. (Sources 2, 7.)
- **Critical read-only nuance:** PodBridge must **never open a stream on the
  capture endpoint itself** (i.e. never call `IAudioClient::Initialize` /
  `Start` on the AirPods mic), because *that* would itself open the microphone and
  **force the very HFP switch we are trying to observe**. Detection must stay at
  enumeration + `GetState` + property reads, which do not open any stream.
  (Reasoned from Source 1's trigger list — opening the input endpoint is trigger #1.)

### A2DP (high quality) vs HFP (call/mono) distinction

- **HFP / call mode (`MicMode.CallModeHfp`)** ⇐ an **active session exists on the
  AirPods capture endpoint** (`AudioSessionStateActive`). Under the unified-endpoint
  model this is precisely when the mic is engaged and stereo A2DP is suspended /
  downmixed to mono. (Sources 1, 3, 6.)
- **High-quality A2DP (`MicMode.HighQualityA2dp`)** ⇐ the AirPods are connected and
  present a render endpoint, and **no active capture session** exists on the
  matched mic endpoint (nor any detectable Communications capture). Playback is
  free to use A2DP. (Source 1.)
- The distinction is the **presence/absence of an active capture session**, not a
  format read. Format is unreliable — see Disputes.

### When the result is `Unknown` (honest, first-class)

`MicMode.Unknown` is returned — never a guessed value — when read-only state does
not determine the profile:

- **Trigger #2 is not read-only-observable.** A render stream opened with category
  *Communications* forces HFP **without any capture session** (Source 1), but no
  documented read-only API exposes a render session's stream **category**:
  `IAudioSessionControl2` surfaces process id / grouping / system-sounds flag but
  **not** `AudioCategory`. So a Communications-only HFP switch (e.g. some VoIP
  playback paths) cannot be positively confirmed read-only ⇒ report `Unknown`
  rather than falsely reporting A2DP.
- **Endpoint matching fails.** If the AirPods expose no capture endpoint that can
  be matched to the render endpoint (A2DP-only presentation, or
  `PKEY_Device_ContainerId` grouping differs across Bluetooth-adapter driver
  stacks / Windows builds), the mic state is indeterminate ⇒ `Unknown`. (Source 4
  is the mechanism; its cross-driver reliability is the risk.)
- **General adapter/build variance.** Endpoint presentation and HFP wideband
  behaviour vary by radio and Windows 11 build (Source 1 footnotes) — any read
  that cannot be trusted degrades to `Unknown`.
- **Distinct from "no device":** when **no** AirPods audio endpoint is present at
  all (disconnected), that is the neutral **"no device"** state per the spec, not
  `Unknown`.

### Confirmation it is read-only (switches nothing)

Every call in the recommended path is a query: device/endpoint **enumeration**,
**property reads** (`PKEY_Device_ContainerId`, format keys), session
**enumeration** (`GetSessionEnumerator`) and session **state** reads (`GetState`).
No `SetDefaultEndpoint`, no `IAudioClient::Initialize/Start`, no `IPolicyConfig`.
PodBridge observes the mode the OS already chose and **never changes the active
profile** — endpoint switching and the HiFi-lock / auto-switch / call-mode policy
are Phase 4. (Sources 1, 2, 3, 7.)

## Recommended approach (for issue #23)

Behind the read-only `IAudioStateReader` (Core) → `WindowsAudioStateReader`
(Windows), returning `MicMode { HighQualityA2dp, CallModeHfp, Unknown }`:

1. Enumerate active render + capture endpoints via `IMMDeviceEnumerator`
   (`DEVICE_STATE_ACTIVE`). Identify the AirPods endpoints (reuse the Phase-1
   connected-device signal; match audio endpoints to it, e.g. by name / address /
   container id).
2. Match the AirPods **render** and **capture** endpoints by
   `PKEY_Device_ContainerId`. If no capture endpoint can be matched → `Unknown`
   (or "no device" if nothing is present).
3. On the **capture** endpoint, `Activate(IAudioSessionManager2)` →
   `GetSessionEnumerator` → if any `IAudioSessionControl.GetState() ==
   AudioSessionStateActive` → **`CallModeHfp`**; else → **`HighQualityA2dp`**.
4. Never open a stream on the capture endpoint; never call any switching API.
5. Read on device-connect and on the manual "Refresh audio status" action (spec
   default cadence); event-driven session monitoring is Phase 4.
6. Unit-test the read→display mapping with a **fake `IAudioStateReader`** covering
   all three states (`HighQualityA2dp` → "high quality (A2DP)",
   `CallModeHfp` → "call mode (mono)", `Unknown` → honest "couldn't determine").

## Disputes (minority → majority decision)

- **Format/sample-rate read vs session-activity read.** A common community
  suggestion is to infer the mode from the endpoint's format (e.g. mono 16 kHz ⇒
  HFP). **Rejected as the primary signal:** Source 5 (authoritative) shows
  `PKEY_AudioEngine_DeviceFormat` is the **user-selected static "Default Format"**,
  not a live per-profile value, and `GetMixFormat` returns the engine's
  shared-mode mix format (closely tied to that default), not a documented live
  A2DP↔HFP flip. → **Majority: detect via active capture-session state
  (Sources 1–3); treat any format reading as at most a weak corroborator, never
  the decision.**
- **Can the Communications render trigger be read?** Minority hope: read the render
  session's category to catch trigger #2. **No documented read-only API exposes a
  session's `AudioCategory`** (`IAudioSessionControl2` lacks it). → **Majority:
  accept trigger #2 as a genuine blind spot and return `Unknown` for it rather
  than guessing** — consistent with the constitution's honest-audio-surface rule.
- **Is `IPolicyConfig` needed?** Some audio-switching samples (Source 7) center on
  `IPolicyConfig`. **Not for reading.** It is a *set* API (Phase 4). → **Majority:
  the Phase-3 reader uses only documented read-only CoreAudio interfaces; no
  `IPolicyConfig`.**
- **Elevation.** No source indicates any of the read calls require admin;
  `IAudioSessionManager2` is a normal desktop-app interface (Source 2). →
  **Consensus: driver-free and admin-free (`asInvoker`), matching Tier-1.**
