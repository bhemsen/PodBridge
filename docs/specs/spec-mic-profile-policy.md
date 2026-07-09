# Spec: Microphone-profile policy (Phase 4)

> Created: 2026-07-09

Give the user explicit control over the unavoidable A2DP↔HFP trade-off on AirPods
via a driver-free microphone-profile policy with three modes — **HiFi-lock**
(AirPods stay on A2DP, the communications mic is another device), **Auto-switch**
(AirPods are promoted to the communications role only while a comms capture
session is live, then restored), and **Call-mode** (a manual tray toggle that
swaps the render+capture communications role together). The policy decision logic
lives in `PodBridge.Core` behind `IAudioPolicy` + `IAudioSessionMonitor`; the
Windows levers are `IPolicyConfig`/`IPolicyConfig2` (set default vs
default-communications endpoint per role) and `IAudioSessionManager2` (observe
comms capture sessions), both via P/Invoke with no admin and no driver. This spec
carries no lifecycle state — acceptance is the spec merged on the default branch
with a milestone and issues; all progress lives in the GitHub issues and
milestone. A completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] A **mic-policy engine in `PodBridge.Core`** exposes the three modes
      (HiFi-lock / Auto-switch / Call-mode) behind `IAudioPolicy` (set default vs
      default-communications endpoint per role; enumerate endpoints) and
      `IAudioSessionMonitor` (comms-capture-session open/close events) — no
      P/Invoke, no WinRT-UI package in Core.
- [ ] Each enumerated endpoint carries an **adapter-supplied `isAirPods` flag** so
      the engine knows which render/capture endpoint IS the AirPods; all role
      routing ("the AirPods endpoint" vs "a non-AirPods device") and the
      single-device degrade trigger hinge on this flag. The identification is a
      **distinct mapping** from the Bluetooth-device identification of Phases 1–2
      (name heuristic / BLE company-id) — it maps an *audio endpoint*, not a
      *Bluetooth device*.
- [ ] **HiFi-lock** keeps AirPods as the default (media/console) render endpoint
      while the default-communications render **and** capture endpoints point at a
      non-AirPods device, so opening a comms mic session never forces HFP on the
      AirPods (media stays A2DP). Observable: with a call app open, AirPods media
      audio stays A2DP and the mic used is the fallback device.
- [ ] **Auto-switch** promotes AirPods to the default-communications role
      (render+capture) when a comms capture session opens and **restores** the
      HiFi-lock role assignment when the session closes. Observable: AirPods mic
      works during a call (mono/HFP), A2DP media resumes after the call.
- [ ] **Call-mode** is a manual tray toggle that swaps the render+capture
      communications role to/from AirPods on demand, independent of any live
      session. Observable: toggling on gives AirPods mic; toggling off returns
      AirPods to A2DP-preferred roles.
- [ ] The **selected mode is persisted** across restarts (default **HiFi-lock**),
      and the active mode is selectable from the **tray** (submenu +
      Call-mode toggle).
- [ ] The engine's mode transitions and endpoint-role decisions are covered by a
      **device-independent unit test**: fake `IAudioPolicy` + fake
      `IAudioSessionMonitor` drive all three modes and a comms-session
      open/close cycle, asserting the exact endpoint-role assignments and restore
      — **including the single-device degrade decision** (a fake `IAudioPolicy`
      exposing only an AirPods endpoint collapses HiFi-lock/Auto-switch to
      Call-mode behaviour and raises the honest warning), since that branch is
      pure, fakeable engine logic (constitution's Tier-1 test gate).
- [ ] **Graceful degradation:** when AirPods are the only audio device (no
      non-AirPods fallback for the comms role), HiFi-lock/Auto-switch degrade to
      Call-mode behaviour and the tray surfaces an honest warning
      ("no alternate mic — AirPods mic requires HFP/mono") — never a silent
      quality collapse or crash.
- [ ] Runs **driver-free and without elevation** (`asInvoker`) — Tier 1.

## Scope

### In scope

- `IAudioPolicy` (Core) + `WindowsAudioPolicy` (Windows adapter, NAudio enumerate
  + `IPolicyConfig`/`IPolicyConfig2` P/Invoke `SetDefaultEndpoint` per `ERole`).
- **AirPods audio-endpoint identification** in `WindowsAudioPolicy`: tag each
  enumerated render/capture endpoint as AirPods vs non-AirPods by matching the
  MMDevice **container-id** (`PKEY_Device_ContainerId`) to the connected AirPods,
  with an endpoint friendly-name fallback, and surface it as an `isAirPods` flag
  on the endpoint model so Core's role routing and the degrade trigger have a
  definite per-endpoint answer.
- `IAudioSessionMonitor` (Core) + `WindowsAudioSessionMonitor` (Windows adapter,
  `IAudioSessionManager2` session enumeration + events) detecting a
  Communications-role capture session opening/closing.
- The Core mic-policy engine implementing the three modes + restore semantics.
- Mode persistence and the tray UI (mode submenu + Call-mode toggle + the
  degrade warning).

### Out of scope

- **Negotiated-codec (AAC/SBC) detection and the active-mic-mode _display_ /
  guidance** — that is **Phase 3** (Audio transparency). Phase 4 *acts on* the
  trade-off; Phase 3 *detects and shows* the codec/mic state. Phase 4 consumes no
  codec value of its own (this is why there is no milestone edge to Phase 3).
- **Battery %, in-ear state, auto play/pause and any BLE-advertisement
  telemetry** — **Phase 2**; the media-session pause/resume plumbing is Phase 2's,
  not reused here. Phase 2's BLE company-id device match is also **not** the
  audio-endpoint identification this phase needs (different layer).
- **Installer/MSIX, winget, start-at-login, the not-affiliated disclaimer /
  About surface** — **Phase 5** (Packaging).
- **ANC/Transparency switching, gesture remap, and anything over the L2CAP
  kernel driver** — **Phases 6–7** (advanced tier). This phase touches no AAP
  opcode and no `DriverAapTransport`.
- Forcing a wideband (mSBC) mic or any codec negotiation — radio/driver-negotiated
  and not controllable from user mode (prior-art).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10; `Core` is OS-free with no P/Invoke; audio adapters live in
  `PodBridge.Windows`; composition root in `PodBridge.App`; Apache-2.0;
  warnings-as-errors in Core; max 50-line functions; nullable enabled).
- Component boundaries per `docs/architecture.md`, key flow #2 (mic-profile
  policy): `WindowsAudioSessionMonitor` detects a Communications capture session →
  Core policy engine decides per active mode → `WindowsAudioPolicy` sets default
  vs communications endpoint per role → restores on release. This phase
  **implements** the already-named `IAudioPolicy`, `IAudioSessionMonitor`,
  `WindowsAudioPolicy`, and `WindowsAudioSessionMonitor`; `docs/architecture.md`
  is updated (living doc) if any signature detail changes (e.g. the endpoint
  `isAirPods` flag).
- **Audio-endpoint identification is a distinct mapping** from Phases 1–2: the
  Phase-1 name heuristic and Phase-2 BLE company-id identify a *Bluetooth device*,
  not an *audio endpoint*. The engine never invents this mapping — it consumes an
  `isAirPods` flag the Windows adapter derives (see Prior decisions).
- `IPolicyConfig`/`IPolicyConfig2` are **undocumented, reverse-engineered** COM
  interfaces — the GUID/vtable/`SetDefaultEndpoint` signature must be confirmed
  from sources (research-intensive; see Prior decisions) and isolated in the
  Windows adapter; NAudio can enumerate endpoints but **cannot** set defaults.
- Tier 1 needs no admin/driver: `asInvoker` manifest; the whole feature works
  with the advanced driver absent (constitution: graceful degradation).
- Verify = `powershell -NoProfile -File build/verify.ps1` (build + format +
  `dotnet test`); it is the per-iteration gate and must stay green.

## Prior art

- [Windows audio-policy plumbing (IPolicyConfig, IMMDevice, IAudioSessionManager2)](../prior-art.md#windows-audio-policy-plumbing-ipolicyconfig-immdevice-iaudiosessionmanager2)
  — the driver-free levers for exactly this feature: Win11 unifies A2DP/HFP into
  one render + one capture endpoint; HFP is forced when any app opens the mic **or**
  opens a `Communications`-category render stream; a user-mode tool cannot command
  the profile but CAN set default vs default-communications endpoints per role and
  watch `IAudioSessionManager2` events. It names the three strategies this phase
  implements (HiFi-lock / Auto-switch / Call-mode toggle) and warns that forced
  wideband mic and LE Audio/LC3 do not apply to Classic-only AirPods.
- [Reference switchers (Reconnect-AirPods, SoundSwitch, EarTrumpet, AudioDeviceCmdlets)](../prior-art.md#reference-switchers-reconnect-airpods-soundswitch-eartrumpet-audiodevicecmdlets)
  — proven `IPolicyConfig` role-switching prior art (Reconnect-AirPods = concrete
  embedded-C# `IPolicyConfig`; SoundSwitch = swap playback+communication device
  together = the Call-mode-toggle model). Confirms **no** integrated AirPods-aware
  auto-switcher exists to fork — this phase builds the genuine gap.
- [Windows audio codec & quality](../prior-art.md#windows-audio-codec--quality)
  — why HiFi-lock matters: native AAC A2DP is the AirPods quality ceiling on
  Win11 and collapses to HFP/narrowband when the mic is used; also the source of
  the "never promise Apple-parity / forced-AAC" honesty constraint. Detection of
  the codec itself is Phase 3, referenced here only for the trade-off rationale.
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — audio default-device switching is done everywhere via undocumented
  `IPolicyConfig`/`IPolicyConfig2` P/Invoke (NAudio enumerates but cannot set
  defaults); confirms the adapter approach.

## Human prerequisites

- [ ] none — no secrets, accounts, certificates, or external provisioning. The
      feature is driver-free Tier 1; the EV-cert / driver-signing prerequisite
      belongs to the Phase 6 advanced tier, not here. (Real AirPods + a second
      audio device are exercised at the human QA gate, not a provisioning
      prerequisite.)

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Policy decision logic lives in `PodBridge.Core` behind `IAudioPolicy` + `IAudioSessionMonitor`; all P/Invoke sits in `WindowsAudioPolicy`/`WindowsAudioSessionMonitor` | Constitution: Core is OS-free and unit-testable; architecture key flow #2 already draws this boundary | 2026-07-09 |
| HiFi-lock = AirPods stay default (media) render; default-**communications** render **and** capture set to a non-AirPods device | Prior-art plumbing entry: HFP is forced by a mic session OR a Communications render stream, so both comms roles must avoid AirPods to keep A2DP | 2026-07-09 |
| Auto-switch = promote AirPods to the comms role on a comms capture-session open, restore the HiFi-lock assignment on close | Deterministic, tool-driven switch/restore rather than relying on Windows renegotiation timing; matches the roadmap "flip on a comms capture session" intent | 2026-07-09 |
| Call-mode swaps render+capture communications role **together** (SoundSwitch model) | Prior-art names this the most robust manual model; a single user action, no session race | 2026-07-09 |
| The `IPolicyConfig`/`IPolicyConfig2` P/Invoke contract is **research-intensive** → `chore:research-ipolicyconfig` (GUID, vtable order, `SetDefaultEndpoint` + `ERole` signature, Win11 build variance, **and the MMDevice→device match**) + an implementation issue depending on it | Contract pre-classifies undocumented Windows-audio API confirmation (≥3 lookups) as research-intensive; the interface is reverse-engineered and build-fragile, and endpoint-to-device matching is the same IMMDevice surface | 2026-07-09 |
| Comms-capture-session detection via `IAudioSessionManager2` is **research-intensive** → `chore:research-comms-session-detection` (session-role identification, the exact A2DP→HFP forcing trigger, event vs poll reliability) + an implementation issue depending on it | Same contract rule; distinct API surface from IPolicyConfig, so a separate research unit | 2026-07-09 |
| **DEFAULT (open):** AirPods audio-endpoint identification — the `WindowsAudioPolicy` adapter tags each enumerated render/capture endpoint as AirPods vs non-AirPods by matching the MMDevice **container-id** (`PKEY_Device_ContainerId`) to the connected AirPods, else by endpoint friendly-name, and exposes it as an `isAirPods` flag; Core routes roles purely on that flag | The Phase-1 name heuristic and Phase-2 BLE company-id identify a *Bluetooth device*, not an *audio endpoint* — a distinct mapping every mode (HiFi-lock, Auto-switch, Call-mode, fallback pick) and the degrade trigger hinge on; container-id is the documented MMDevice→device link, keeping Core OS-free | 2026-07-09 |
| **DEFAULT (open):** default mode is **HiFi-lock** | Vision "great by default" + "honest audio surface": preserve the A2DP quality ceiling unless the user opts into the mic; least-surprise for a media-first user | 2026-07-09 |
| **DEFAULT (open):** when AirPods are the only audio device, HiFi-lock/Auto-switch degrade to Call-mode behaviour with an honest tray warning | Graceful-degradation principle; there is no fallback comms device to route to, and silently forcing HFP would violate the honesty constraint | 2026-07-09 |
| **DEFAULT (open):** the fallback comms device is the current non-AirPods default-communications device (else the first available non-AirPods capture/render device) | Least-configuration sensible pick; avoids a mandatory device picker in Phase 4 (a picker can be a later QoL adhoc) | 2026-07-09 |
| **DEFAULT (open):** Auto-switch restore target on session close is the **HiFi-lock** role assignment (not "whatever was there before") | Deterministic, self-healing end state; avoids drift if roles were changed mid-call | 2026-07-09 |
| **DEFAULT (open):** the selected mode is persisted in the app's local user settings; the tray reflects it on next launch | Consistency with a tray-first background app; no network, local-only per constitution | 2026-07-09 |
| Mode selection UI is the **tray** submenu + a Call-mode toggle item (no settings window) | Matches Phase-1 tray-only UX; the full settings/About window is Phase 5 | 2026-07-09 |
| `asInvoker` manifest, no driver | Constitution: Tier 1 needs no admin | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue
per implementable step, grouped under a milestone. This spec owns the design; the
issues own progress.

- Milestone: created on merge (one per this phase). Its description carries
  `Depends on milestone: #1` — **Phase 1 (Foundation & pairing)**, whose tray
  shell, generic host, and composition root this phase extends (mode submenu,
  Call-mode toggle, mode persistence). It does **not** depend on Phase 3: Phase 4
  *acts on* the A2DP↔HFP trade-off while Phase 3 *detects and displays* the
  codec/mic state (Scope), and neither consumes the other, so the two milestones
  are **independent** and may run as parallel orchestrators (workflow: edgeless
  milestones run in parallel).
- Issues: created from this spec once merged (one per implementable step)

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and unit tests all green.
- [ ] A **device-independent unit test** drives the engine with a fake
      `IAudioPolicy` + fake `IAudioSessionMonitor` through all three modes and a
      comms-session open→close cycle, asserting the exact endpoint-role
      assignments and the Auto-switch restore (constitution Tier-1 test gate).
- [ ] The **no-fallback degrade decision is unit-tested** (same gate): a fake
      `IAudioPolicy` exposing **only an AirPods endpoint** (no non-AirPods
      fallback) makes HiFi-lock and Auto-switch collapse to Call-mode behaviour
      and raise the honest "no alternate mic" warning — asserted with no physical
      device (the branch is pure engine logic, per the constitution's no-device
      test rule).
- [ ] The **`chore:research-ipolicyconfig` research comment** is posted and its
      consensus (interface GUID, vtable order, `SetDefaultEndpoint`/`ERole`
      signature, and the MMDevice→device container-id match) is reflected in
      `WindowsAudioPolicy` (contract: research comment as QA artefact).
- [ ] The **`chore:research-comms-session-detection` research comment** is posted
      and its consensus (comms-role session identification + HFP forcing trigger +
      event/poll approach) is reflected in `WindowsAudioSessionMonitor` (contract:
      research comment as QA artefact).
- [ ] **HiFi-lock (Human QA, real HW):** with a call app open, AirPods media audio
      stays A2DP and the microphone in use is the fallback device.
- [ ] **Auto-switch (Human QA, real HW):** opening a comms capture session gives
      the AirPods mic (HFP/mono); closing it restores A2DP media on AirPods.
- [ ] **Call-mode (Human QA, real HW):** the tray toggle swaps AirPods to/from the
      communications role on demand, with no live session required.
- [ ] **Endpoint identification (Human QA, real HW):** with AirPods plus a second
      audio device connected, the adapter tags the correct endpoint as AirPods
      (roles route to the right device); the unit test above already covers the
      routing decision given a correct flag.
- [ ] Selected **mode persists** across an app restart (default HiFi-lock on first
      run).
- [ ] **Degrade path (Human QA, real HW):** with AirPods as the only audio device,
      the tray actually surfaces the honest "no alternate mic" warning and no
      silent HFP occurs — the *decision* is already covered by the unit-test item
      above; this confirms the on-hardware tray behaviour only.
- [ ] The process runs **without elevation** (`asInvoker`) — no admin prompt, no
      driver.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| `IPolicyConfig`/`IPolicyConfig2` GUID/vtable differs or breaks across Windows 11 builds | Confirmed up front by `chore:research-ipolicyconfig`; isolated in `WindowsAudioPolicy` behind `IAudioPolicy`; core logic unit-tested against the fake; verified on real hardware at the QA gate. |
| The wrong endpoint is tagged as the AirPods (multiple containers, docking/virtual audio devices) | Match the MMDevice container-id (`PKEY_Device_ContainerId`) to the connected AirPods with a friendly-name fallback (Prior decision); the technique is confirmed by `chore:research-ipolicyconfig`; Core routes on the adapter-supplied `isAirPods` flag (unit-tested via the fake), and the mapping is verified on real HW at the QA gate. |
| Comms-session detection misses or double-fires, leaving AirPods stuck in HFP or A2DP | `chore:research-comms-session-detection` settles the event model; Auto-switch always restores to a deterministic HiFi-lock end state; Call-mode is the robust manual fallback. |
| No non-AirPods device exists to hold the comms role | Documented degrade to Call-mode behaviour + honest tray warning; **the decision is unit-tested** with a fake single-AirPods-endpoint `IAudioPolicy` (no silent quality collapse or crash). |
| CI cannot exercise Bluetooth/audio routing | CI runs Verify (build/format/unit-with-fakes) only — which **includes the single-device degrade decision and all three-mode routing decisions** against fakes; only the on-hardware behaviour and the actual tray warning are checked by the human QA gate on real hardware with real AirPods + a second device (contract QA-gate default = UI check / smoke test). |
| Users expect a wideband/HiFi mic ("solve" the trade-off) | Honesty constraint: the tray warning and Phase-3 mic-mode display state the HFP/mono reality; we manage the trade-off, we do not claim to fix it (vision non-goal). |

## Decision log

- 2026-07-09: Spec drafted. Five genuinely-open points were settled as documented
  DEFAULTs (autonomous bulk planning — no OPEN rows left): default mode =
  HiFi-lock; single-device degrade to Call-mode + warning; fallback comms device =
  current non-AirPods default-communications; Auto-switch restores to HiFi-lock;
  mode persisted in local user settings. Each is recorded as a Prior-decisions row
  and echoed in the milestone's openDefaults for the spec-acceptance gate to
  confirm or override. Two research-intensive units split out
  (`chore:research-ipolicyconfig`, `chore:research-comms-session-detection`), each
  with a dependent implementation issue and its research comment named as a QA
  artefact.
- 2026-07-09: Addressed spec-review must-fix items. (1) **Fixed AirPods
  audio-endpoint identification** as a documented default — the adapter tags each
  endpoint by container-id (`PKEY_Device_ContainerId`), friendly-name fallback,
  and surfaces an `isAirPods` flag; this is a distinct mapping from the
  Bluetooth-device identification of Phases 1–2. Recorded as a Prior-decisions row
  + a sixth openDefault, added to Scope/Constraints/Outcome, folded the MMDevice→
  device match into `chore:research-ipolicyconfig`, and added a mis-tag risk row.
  (2) **Moved the single-device degrade DECISION into the device-independent
  unit-test gate** (fake `IAudioPolicy` exposing only an AirPods endpoint →
  Call-mode + honest warning), per the constitution's "every Tier-1 feature has a
  no-device test"; only the on-hardware tray-warning confirmation stays at the
  human QA gate, and the CI risk row was corrected accordingly. (3) **Corrected
  the milestone dependency from Phase-3 to Phase-1** (the tray shell + generic
  host + composition root this phase extends); Phase 3 and Phase 4 consume nothing
  of each other's, so they are independent and run in parallel.