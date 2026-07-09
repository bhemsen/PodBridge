# Spec: Audio transparency (Phase 3)

> Created: 2026-07-09

Make PodBridge honest about AirPods sound on Windows: detect the actually-negotiated A2DP codec (AAC vs SBC), surface whether the audio link is currently in high-quality A2DP or collapsed to HFP/call mode, and guide the user toward AAC with generic, driver-free advice — never promising Apple-parity sound and never switching anything (display + advise only). This spec carries no lifecycle state — acceptance is the spec merged on the default branch with a milestone and issues, and all progress lives in the GitHub issues and milestone. A completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] PodBridge reads the **actually-negotiated A2DP codec** for the connected AirPods and exposes it to Core as a first-class value: `Aac`, `Sbc`, or `Unknown` (a genuine, honest state when Windows will not reveal it) — behind a **read-only** `IAudioStateReader` interface (Core) implemented by `WindowsAudioStateReader` (Windows), driver-free and admin-free.
- [ ] PodBridge reads and surfaces the **active microphone / audio-link mode** — high-quality A2DP (stereo media) vs HFP/call mode (mono, mic engaged) vs `Unknown` — **display only**, via the same read-only reader; it never sets or switches an endpoint (that is Phase 4).
- [ ] A **device-independent Core guidance engine** turns the read audio state into honest, actionable guidance: on `Sbc` it advises how to reach AAC (Windows 11 21H2+, update the Bluetooth adapter driver, use an AAC-capable adapter/dongle) and on `Aac` it confirms the best-available quality; it **never** claims Apple-parity sound, never recommends the paid "Alternative A2DP Driver", and never offers a "force AAC" action.
- [ ] The **tray surface** shows a codec line and a mic-mode line, and raises a Windows notification with the AAC guidance **only when SBC fallback is confirmed** (tray + notifications only, consistent with Phase 1 — no separate window until Phase 5).
- [ ] The guidance engine and the read→display mapping are covered by a **device-independent unit test** driving a fake `IAudioStateReader` (constitution Tier-1 test gate), covering **every enum state**: codec `Sbc` → AAC-advice state, `Aac` → best-quality state, `Unknown` → honest "couldn't determine" state; mic-mode `CallModeHfp` → "call mode (mono)", `HighQualityA2dp` → "high quality", `Unknown` → honest "couldn't determine" mic line.
- [ ] **Verify stays green** and the app still runs with an **`asInvoker` manifest** (no elevation) — Tier 1 stays driver-free and admin-free.

## Scope

### In scope

- A **read-only** audio-state surface: `IAudioStateReader` (Core) + `WindowsAudioStateReader` (Windows) returning `(CodecKind, MicMode)`.
- Detection of the **negotiated A2DP codec** (AAC/SBC/Unknown) on Windows 11, driver-free (research-intensive — split, see Prior decisions).
- Detection and **display** of the active mic / audio-link mode (A2DP vs HFP/call vs Unknown), driver-free (research-intensive — split).
- A **Core guidance engine** producing honest AAC-guidance text from the audio state, unit-tested with a fake.
- **Tray display** of codec + mic-mode and a Windows notification carrying the AAC guidance on confirmed SBC fallback.
- Living-doc update: record `IAudioStateReader` / `WindowsAudioStateReader` in `docs/architecture.md` when implemented.

### Out of scope

- **The microphone-profile policy and any endpoint switching — Phase 4.** Phase 3 only *reads and displays* the mic/audio-link mode; HiFi-lock / auto-switch / call-mode, `IAudioPolicy::SetDefaultEndpoint`, and the event-driven `IAudioSessionMonitor` all belong to Phase 4. Phase 3 introduces no ability to change the active profile.
- **BLE battery / in-ear telemetry and the Continuity parser — Phase 2.** Phase 3 assumes the connected-device signal from Phase 1 and adds only the audio surface.
- **Installer, MSIX/winget, the not-affiliated disclaimer / About window — Phase 5.** Phase 3 stays tray + notifications only.
- **The L2CAP kernel driver, ANC/Transparency switching, gesture remap — Phases 6–7.** No AAP L2CAP writes here.
- **Forcing AAC / any "make it AAC" toggle** — no documented Windows switch exists; negotiation is radio/driver-dependent (prior-art AVOID). Phase 3 advises, it does not force.
- **Recommending or bundling third-party audio drivers/codecs** (the paid "Alternative A2DP Driver", FDK-AAC) — constitution Don'ts; guidance stays generic.
- **Any Apple-parity sound claim** — explicitly a non-goal (vision + constitution "Honest audio surface").

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md` (C#/.NET 10, WPF tray, `Core` OS-free with no P/Invoke, adapters in `Windows`, composition root in `App`, Apache-2.0, nullable + warnings-as-errors in Core, max 50-line functions).
- Component boundaries per `docs/architecture.md` — `App` depends on `Core` abstractions and wires the `Windows` implementation at the composition root only. This phase adds `IAudioStateReader` (Core) + `WindowsAudioStateReader` (Windows); `docs/architecture.md` is updated to list them when implemented (living doc). Architecture flow 3 ("Codec transparency") is realised here via a dedicated **read-only** reader rather than through `IAudioPolicy`, to keep the Phase-4 switching surface separate.
- **Honest audio surface** (constitution): no user-facing string claims Apple-parity sound; the codec and its limitation are stated truthfully; `Unknown` is shown, not guessed. Verifiable in review.
- **Tier 1 needs no admin/driver:** `asInvoker` manifest; the reader uses only user-mode, driver-free mechanisms (WinRT / documented Windows-audio interfaces / read-only OS state). No elevation, no driver.
- **Graceful degradation** (constitution): with no AirPods connected, the audio surface shows a neutral "no device" state; when the codec/mic mode cannot be determined it shows `Unknown` — never crashes, never fabricates a value.
- **Local-only:** the guidance is static text; no network calls.
- Verify = `powershell -NoProfile -File build/verify.ps1` (~10s baseline); CI runs it on `windows-latest`.
- **Research-intensive units** (workflow contract): both the codec-detection and the mic-mode-detection mechanisms require ≥3 Microsoft-doc / ETW / registry / community-source lookups to confirm a driver-free approach; each is split into a `chore:research-*` issue whose Markdown research comment is the sole content authority for its implementation issue.

## Prior art

- [Windows audio codec & quality](../prior-art.md#windows-audio-codec--quality) — the central entry: Windows 11 21H2+ negotiates AAC A2DP natively (no driver); AAC is the AirPods quality ceiling on Windows; the tool detects the negotiated codec and advises driver/dongle updates on SBC fallback. AVOID list pins the honesty constraints: no Apple-parity claim, no "force AAC" switch, Windows 10 has no AAC.
- [Microphone & audio-profile switching](../prior-art.md#microphone--audio-profile-switching) — Win11 unifies A2DP/HFP into one render + one capture endpoint and forces HFP when an app opens the mic or a `Communications` render stream; `IAudioSessionManager2` / `IMMDevice` are the driver-free levers. Phase 3 uses these **only to read/display** the active mode; the switching strategies (HiFi-lock / auto-switch / call-mode) are Phase 4.
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent) — audio state is read via `IPolicyConfig`/CoreAudio P/Invoke and NAudio enumeration patterns (NAudio enumerates but cannot set defaults); confirms the C#/.NET user-mode approach for the reader.

## Human prerequisites

- [ ] none — no secrets, accounts, certificates, or external provisioning. Phase 3 is Tier-1, driver-free and admin-free; no EV cert is involved (that is a Phase-6 driver concern). Real-AirPods hardware is exercised at the human QA gate, not a provisioning prerequisite.

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Phase 3 introduces a **read-only** `IAudioStateReader` (Core) + `WindowsAudioStateReader` (Windows), separate from Phase 4's `IAudioPolicy` / `IAudioSessionMonitor` | Phase 3 only reads and displays; keeping the switching/policy surface out of this phase keeps the boundary vs Phase 4 tight and the reader trivially unit-testable via a fake | 2026-07-09 |
| Codec model is an enum `CodecKind { Aac, Sbc, Unknown }`; `Unknown` is a first-class honest state | Honesty over guessing (constitution "Honest audio surface"); prior-art AVOIDs unreliable heuristics and any "force AAC" claim — when Windows will not reveal the codec, say so | 2026-07-09 |
| Mic/audio-link mode model is an enum `MicMode { HighQualityA2dp, CallModeHfp, Unknown }`, **display only** | Surfaces the unavoidable A2DP↔HFP trade-off truthfully; switching is Phase 4 (vision non-goal: we manage, not solve, the trade-off) | 2026-07-09 |
| Phase 3's milestone depends on **Phase 1's milestone (#1) only** (`Depends on milestone: #1`), **not** Phase 2 | Phase 3 consumes only Phase 1's connected-device signal (`IConnectionMonitor`) and adds the audio surface on top; Scope excludes every Phase-2 BLE/battery/Continuity deliverable and nothing in Phase 3 reads one. Pointing the edge at Phase 2 would falsely over-serialize the two orchestrators (`/loopkit:implement` reads `Depends on milestone` for milestone-level parallelism), so the edge must name the milestone Phase 3 actually depends on | 2026-07-09 |
| Codec detection is **research-intensive** → `chore:research-codec-detection` + a dependent implementation issue | No documented public API returns the negotiated A2DP codec; a driver-free approach (ETW BthA2dp provider / registry A2DP keys / endpoint properties) needs ≥3 source lookups and cross-checking (workflow contract) | 2026-07-09 |
| Mic-mode detection is **research-intensive** → `chore:research-mic-mode-detection` + a dependent implementation issue | Reading whether the link is in A2DP vs HFP driver-free (unified-endpoint format/state + `IAudioSessionManager2`) needs multi-source confirmation against Microsoft docs (workflow contract) | 2026-07-09 |
| The **guidance engine lives in Core** (device-independent, unit-tested); `App` only renders its output | Constitution Tier-1 test gate + `Core` OS-free; the SBC→advice / AAC→confirm / Unknown→honest mapping is pure logic testable with a fake reader | 2026-07-09 |
| Guidance is **generic**: Windows 11 21H2+, update the Bluetooth adapter driver, prefer an AAC-capable adapter/dongle; it never recommends the paid "Alternative A2DP Driver" nor bundles FDK-AAC, and offers **no "force AAC" action** | Constitution Don'ts + prior-art AVOID (zero net benefit and licensing burden of the paid driver; negotiation is radio/driver-dependent so there is no honest force switch) | 2026-07-09 |
| **DEFAULT** — Audio status is surfaced as tray context-menu lines (codec + mic-mode) plus a Windows notification carrying the AAC guidance **only on confirmed SBC fallback** (not on `Aac`, not on `Unknown`) | Consistent with Phase 1's "tray + notifications only, no separate window until Phase 5"; avoids nagging when quality is already best or genuinely unknown | 2026-07-09 |
| **DEFAULT** — Audio state is read **on device-connect and on a manual "Refresh audio status" menu action**, not by continuous polling | The codec is negotiated at connect; low-invasiveness (vision) favours event + on-demand reads over a polling loop; mic-mode transitions are Phase 4's event concern | 2026-07-09 |
| **DEFAULT** — When codec or mic-mode is `Unknown`, the tray shows a neutral honest line ("Codec: couldn't determine") and **suppresses** the AAC advice notification | Honesty (constitution) — never guess a codec; only advise when SBC is positively confirmed, so the advice is always trustworthy | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue per implementable step, grouped under this phase's milestone. This spec owns the design; the issues own progress.

- Milestone: created on merge (one per this phase); carries `Depends on milestone: #1` (Phase 1's milestone — Phase 3 consumes only Phase 1's connected-device signal, no Phase-2 deliverable) in its description.
- Issues: created from this spec once merged (one per implementable step). The two `chore:research-*` issues are listed first, each with its implementation dependent.

## Verification

This list doubles as the script for the human milestone-QA gate; while the test suite is still thin, every acceptance item no machine check covers is verified by the human here.

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build (Release), `dotnet format --verify-no-changes`, and unit tests all green.
- [ ] A **fake `IAudioStateReader`** drives a device-independent unit test asserting the guidance/display mapping for **every enum state**: codec `Sbc` → AAC-advice state, `Aac` → best-quality state, `Unknown` → "couldn't determine" state; mic-mode `CallModeHfp` → "call mode (mono)", `HighQualityA2dp` → "high quality", `Unknown` → honest "couldn't determine" mic line (constitution Tier-1 test gate).
- [ ] The **codec-detection research comment** (from `chore:research-codec-detection`) is posted, reaches documented consensus on the driver-free detection mechanism and the `Unknown` fallback, and its consensus is reflected in `WindowsAudioStateReader` (contract: research comment as QA artefact; implementer does no additional WebSearch).
- [ ] The **mic-mode-detection research comment** (from `chore:research-mic-mode-detection`) is posted, reaches documented consensus on the driver-free A2DP-vs-HFP read, and its consensus is reflected in `WindowsAudioStateReader` (contract: research comment as QA artefact).
- [ ] **(Human QA gate)** On a real machine with real AirPods: when AAC is negotiated the tray shows "Codec: AAC" and no advice notification; when the link falls back to SBC the tray shows "Codec: SBC" and a notification offers the generic AAC guidance.
- [ ] **(Human QA gate)** Opening a call/mic app (a `Communications` capture session) flips the tray mic-mode line to "Call mode (mono)"; releasing it returns to "High quality (A2DP)" after a refresh — **displayed only, nothing switched by PodBridge**.
- [ ] No user-facing string claims Apple-parity sound, recommends the paid "Alternative A2DP Driver", or offers a "force AAC" action (review, per constitution "Honest audio surface" + Don'ts).
- [ ] With **no AirPods connected** the audio surface shows a neutral "no device" state; with the codec undeterminable it shows "Codec: couldn't determine" — **no crash, no fabricated value**.
- [ ] The process runs **without elevation** (`asInvoker`) and installs/needs **no driver** — verified by a smoke run.
- [ ] `docs/architecture.md` lists `IAudioStateReader` / `WindowsAudioStateReader` (living-doc update).

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows exposes **no clean public API** for the negotiated A2DP codec, so detection may be fragile or impossible on some stacks | Confirmed up front by `chore:research-codec-detection` (ETW / registry / endpoint-property avenues cross-checked); `Unknown` is a first-class honest fallback so the feature degrades gracefully instead of lying or crashing. |
| Codec/mic detection **varies across Bluetooth adapter drivers and Windows builds** | Hidden behind `IAudioStateReader` with a fake for unit tests; the honest `Unknown` state absorbs variance; real behaviour verified at the human QA gate on real hardware. |
| Mic-mode read **overlaps Phase 4** and could creep into switching | Prior decisions pin Phase 3 to **read-only**; no `IAudioPolicy` / `SetDefaultEndpoint` / session-event monitoring here — scope explicitly defers all switching to Phase 4. |
| Guidance could drift into **dishonest or prohibited advice** (Apple parity, paid driver, force-AAC) | Guidance text lives in Core, is unit-testable, and is review-gated against the constitution's Honest-audio-surface principle and Don'ts; advice fires only on positively-confirmed SBC. |
| CI cannot exercise Bluetooth audio | Verify runs build/format/unit only; codec and mic-mode behaviour is checked at the human QA gate (contract QA-gate default = UI check / smoke test on real hardware). |

## Decision log

- 2026-07-09: Spec drafted. All design questions settled from vision/constitution/prior-art or resolved with documented sensible defaults (audio-status surface = tray lines + SBC-only notification; read cadence = on-connect + manual refresh; `Unknown`-codec behaviour = honest line, advice suppressed) — recorded as Prior-decisions rows and surfaced as openDefaults for the spec-acceptance gate. Two research-intensive units split (`chore:research-codec-detection`, `chore:research-mic-mode-detection`) with their research comments named as QA artefacts. Human prerequisites: none.
- 2026-07-09: Addressed spec-review must-fix findings. (1) Corrected the milestone dependency to point at **Phase 1's milestone (#1)** (`Depends on milestone: #1`), matching the stated scope — Phase 3 consumes only Phase 1's connected-device signal and no Phase-2 deliverable — instead of `#<Phase-2 milestone>`, which would have over-serialized the two independent orchestrators (`/loopkit:implement` reads `Depends on milestone` for milestone-level parallelism); recorded as a new Prior-decisions row. (2) Extended the device-independent Tier-1 test matrix to cover `MicMode.Unknown` → honest "couldn't determine" mic line, so every enum state is asserted (mirroring the codec `Unknown` coverage) in the Outcome test-gate item, the Verification item, and the Core guidance-engine issue.