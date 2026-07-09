# Spec: Battery & auto play/pause (Phase 2)

> Created: 2026-07-09

Add PodBridge's first passive-BLE telemetry: a WinRT `BluetoothLEAdvertisementWatcher`
scanner that filters on Apple's company id `0x004C`, a clean-room Apple-Continuity
(proximity-pairing) parser in Core that decodes per-bud + case battery and
in-ear/out-of-ear state, tray battery display for both buds and the case, and
automatic media play/pause driven by in-ear/out-of-ear transitions — all Tier 1
(driver-free, no admin). Company id `0x004C` is the AirPods identifier on the
**advertisement path** (BLE telemetry); it is **added alongside — not a
replacement for** — Phase 1's name-based paired/connected detection
(`IConnectionMonitor`), because the company id rides in the advertisement and
does not appear in the paired-device connection enumeration. Advertisement
tracking, battery display, and play/pause are **gated on the Phase-1 connection
state**, so only the user's connected AirPods drive the tray. This spec carries
no lifecycle state — acceptance is the spec merged on the default branch with a
milestone and issues; all progress lives in the GitHub issues and milestone. A
completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] A **WinRT `BluetoothLEAdvertisementWatcher`** scanner (`WinRtBleScanner`
      in `PodBridge.Windows`, implementing Core's `IBleScanner`) receives BLE
      advertisements **filtered on Apple company id `0x004C`**, driver-free and
      **without elevation** (`asInvoker`, Tier 1).
- [ ] A **clean-room Apple-Continuity parser** in `PodBridge.Core` decodes the
      `0x004C` proximity-pairing payload into per-bud (left/right) battery %, case
      battery %, per-bud charging flags, and in-ear/out-of-ear state, each opcode/
      offset annotated with the documented fact it derives from (constitution:
      clean-room protocol).
- [ ] **On the advertisement path, AirPods are identified by the `0x004C` company
      id** (plus proximity-message model bytes). This is the identifier for **BLE
      telemetry only**; it is **added alongside** Phase 1's name-based
      `IConnectionMonitor` (which is unchanged) and does not replace it — the
      company id does not appear in the paired-device connection enumeration, so it
      cannot drive connection status.
- [ ] **Advertisement tracking, battery display, and play/pause are gated on the
      Phase-1 connection state**: PodBridge only tracks the strongest-RSSI `0x004C`
      advertisement (and drives play/pause) **while `IConnectionMonitor` reports an
      AirPods device connected**; with no AirPods connected, the tray shows no live
      battery and no play/pause fires even when `0x004C` advertisements are in range.
- [ ] The **tray shows battery** for **left bud, right bud, and case** as
      percentages with a charging indicator, and an explicit **"unknown / out of
      range"** state when no fresh advertisement is available (or no AirPods are
      connected).
- [ ] **Automatic play/pause**: removing a bud (out-of-ear) **pauses** active
      media; re-inserting it **resumes** — but only if PodBridge initiated the
      pause (it never resumes media the user paused). Driven via a driver-free
      Windows media-session adapter (`WindowsMediaController` implementing Core's
      `IMediaController`).
- [ ] The **Continuity parse + DeviceState pipeline** is covered by a
      **device-independent unit test**: a fake `IBleScanner` feeds captured/
      synthetic `0x004C` advertisement byte fixtures and the test asserts the
      decoded battery + in-ear state, **and** that a fake `IConnectionMonitor`
      reporting "disconnected" gates the advertisement out (no live battery)
      (constitution Tier-1 test gate).
- [ ] The **auto play/pause engine** is covered by a **device-independent unit
      test**: a fake `IMediaController` records calls while the test drives in-ear/
      out-of-ear transitions and asserts pause-on-remove / resume-on-reinsert, the
      "don't resume user-paused media" rule, **and** that no call fires while the
      fake `IConnectionMonitor` reports no AirPods connected (constitution Tier-1
      test gate).
- [ ] **Verify stays green** and the process runs **without admin**.

## Scope

### In scope

- `IBleScanner` (Core interface) emitting raw BLE advertisement data
  (address, RSSI, manufacturer company id + payload bytes) and `WinRtBleScanner`
  (`PodBridge.Windows`) implementing it via `BluetoothLEAdvertisementWatcher`
  with a `0x004C` manufacturer-data filter.
- `ContinuityParser` + battery/ear/model domain types and a `DeviceState`
  update pipeline in `PodBridge.Core` (decode, dedup, model id, staleness).
- Company-id-based AirPods identification **on the advertisement path** (added
  for BLE telemetry); Phase-1's name-based `IConnectionMonitor` connection
  detection is **unchanged**.
- **Gating** the scanner / battery / play-pause on Phase-1's `IConnectionMonitor`
  connection state (consume the Phase-1 Core interface): the pipeline treats
  advertisements as live only while an AirPods device is connected.
- Tray battery rendering (left/right buds + case %, charging, unknown state) in
  `PodBridge.App`.
- `IMediaController` (Core) + `WindowsMediaController` (`PodBridge.Windows`,
  Windows media-session transport) and the Core auto play/pause engine driven by
  in-ear/out-of-ear transitions.
- Device-independent unit tests for the parse pipeline (including the connection
  gate) and the play/pause engine.

### Out of scope

- Paired/connected status detection, the tray shell, single-instance host,
  pairing-guidance UX and CI — delivered in **Phase 1**. This phase **consumes**
  that shell — including its name-based `IConnectionMonitor` — and **adds**
  company-id-based advertisement telemetry on top of it (it does not rework the
  connection monitor).
- Negotiated-codec (AAC/SBC) detection and active-mic-mode display — **Phase 3**.
- Microphone-profile policy (HiFi-lock / auto-switch / call-mode) and the
  `IAudioPolicy`/audio-session-monitor plumbing — **Phase 4**.
- MSIX/winget packaging, the not-affiliated disclaimer/About surface, and
  auto-start at login — **Phase 5**.
- ANC/Transparency/Adaptive switching, gesture remap, and the L2CAP KMDF driver
  (`IAapTransport`/`DriverAapTransport`) — **Phases 6–7**. In-ear here comes from
  the **advertisement proximity message**, never from L2CAP AAP opcode `0x06`.
- **Cryptographic** binding of an advertisement to the user's specific AirPods,
  and multi-device disambiguation beyond the connection gate + strongest-RSSI
  heuristic — **Phase 8**. The residual crowded-room cross-talk this leaves is a
  documented limitation (see Prior decisions / Risks).
- Broadened model/firmware coverage and diagnostics — **Phase 8** (Phase 2
  targets the common current AirPods models; firmware-specific quirks are
  hardened later).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10; BLE Tier 1 = WinRT `BluetoothLEAdvertisementWatcher` via CsWinRT;
  `Core` OS-free with no P/Invoke; adapters in `Windows`; composition root in
  `App`; Apache-2.0; warnings-as-errors and max 50-line functions in Core).
- **Clean-room protocol** (constitution): every Continuity offset/opcode/constant
  lives in one parser module, each annotated with the documented fact it comes
  from; **no GPL source or verbatim doc prose** copied from AirPodsDesktop,
  librepods, or CAPoD.
- Component boundaries per `docs/architecture.md`: parsing and the play/pause
  decision are **Core** (device-independent, unit-tested via fakes); WinRT and
  media-session access are **Windows** adapters behind Core interfaces; wiring
  lives at the `App` composition root only. This phase adds `IBleScanner`/
  `WinRtBleScanner`, `IMediaController`/`WindowsMediaController`, `ContinuityParser`
  and the battery/ear/`DeviceState` types, and **consumes Phase-1's
  `IConnectionMonitor`** (Core) to gate telemetry; `docs/architecture.md` is
  updated to list the new components when implemented (living doc).
- **Tier 1 needs no elevation/driver** (constitution): `asInvoker`, no
  capability requiring admin. If the research confirms a Windows capability is
  needed for advertisement scanning, it must be a non-admin capability or the
  limitation is documented — no elevation is requested.
- **Graceful degradation** (constitution): when advertisements stop (AirPods out
  of range / disconnected / lid closed) or no AirPods are connected, battery goes
  **stale after a timeout** and the tray shows "unknown / out of range" — never a
  crash and never a stale number presented as live.
- **Local-only** (constitution): the scanner reads local BLE only; no network.
- Verify = `powershell -NoProfile -File build/verify.ps1` (build Release +
  `dotnet format --verify-no-changes` + `dotnet test`); run after every change
  set until green.

## Prior art

- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — AirPodsDesktop **proves** the exact driver-free path this phase builds: WinRT
  `BluetoothLEAdvertisementWatcher` + Apple-Continuity `0x004C` parsing → battery
  + in-ear + auto play/pause with no admin/driver; CAPoD is a mature BLE-
  advertisement battery decode. **Patterns only** — both are GPL-family, so this
  Apache-2.0 tree reimplements clean-room (no code/prose copied).
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — confirms `BluetoothLEAdvertisementWatcher` is the sanctioned driver-free /
  no-admin Tier-1 telemetry surface, and that ANC/gestures are **not** obtainable
  from advertisements (they belong to the later driver tier) — bounding what this
  phase can deliver.
- [AAP / AACP protocol reverse-engineering](../prior-art.md#aap--aacp-protocol-reverse-engineering)
  — librepods' documented **facts** (Apple company id, proximity-pairing message
  layout, battery/in-ear semantics) are reused as facts for the clean-room parser.
  Note the distinction: this phase parses the **BLE advertisement Continuity
  proximity message**, not the L2CAP AAP battery opcode `0x04`/in-ear `0x06`
  (which need the Phase-6 driver). No GPL prose/code copied.

## Human prerequisites

- [ ] none — no secrets, accounts, certificates, or external provisioning. Passive
      BLE advertisement scanning needs no key or account. (An EV code-signing
      certificate is a **Phase-6** driver-signing prerequisite, not this phase.
      Real-AirPods hardware is exercised at the human QA gate, not a provisioning
      prerequisite.)

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| BLE telemetry via WinRT `BluetoothLEAdvertisementWatcher` (Tier 1, driver-free, no admin) | Constitution tech-stack row + architecture Key-flow 1; prior-art proves it on AirPodsDesktop/CAPoD | 2026-07-09 |
| Identify AirPods **on the advertisement path** by Apple company id `0x004C` (+ proximity model bytes); Phase-1's name-based `IConnectionMonitor` connection detection is **unchanged** | Company id `0x004C` rides in the BLE advertisement, not in the paired-device connection enumeration, so it identifies *telemetry* but cannot drive connection *status*; the two surfaces are complementary — company-id identification is **added**, not a replacement for the Phase-1 name match | 2026-07-09 |
| **Gate** advertisement tracking + battery + play/pause on Phase-1's `IConnectionMonitor` connection state (only track while an AirPods device is connected) | Prevents cross-talk: a stranger's AirPods advertising nearby must not drive this user's tray battery or play/pause, and nothing should be tracked when no AirPods are connected; the Phase-1 connection state is the authoritative "these are the user's AirPods" signal that already exists | 2026-07-09 |
| Parsing lives in **Core** (`ContinuityParser`); the scanner adapter emits **raw** advertisement bytes behind `IBleScanner` | Architecture principle: Core is OS-free and unit-testable; keeps decode logic device-independent | 2026-07-09 |
| In-ear/out-of-ear is derived from the **advertisement proximity message**, not L2CAP AAP `0x06` | The `0x06` opcode needs the Phase-6 driver; the proximity message carries in-ear/lid bits and keeps this phase Tier 1 | 2026-07-09 |
| Media control via the **Windows media-session manager** (`GlobalSystemMediaTransportControlsSessionManager`, GSMTC) behind `IMediaController` | Driver-free / no-admin, and it exposes current playback state so we avoid resuming media that was already paused; preferred over synthesizing media-key input | 2026-07-09 |
| **Resume only when PodBridge paused** (track "paused-by-us"); never resume media the user paused | Matches Apple behaviour and avoids surprising the user; the GSMTC playback-state read makes it observable | 2026-07-09 |
| **Pause on the first bud removed** (either bud out-of-ear), resume when it returns in-ear | Mirrors AirPods native behaviour; simplest robust rule for the common single-user case | 2026-07-09 |
| **Single tracked device** in Phase 2, disambiguated by **strongest RSSI** among `0x004C` advertisements **received while an AirPods device is connected** | Apple's rotating random addresses mean an advertisement cannot be cryptographically bound to *your* specific AirPods without the pairing key; gating on connection state removes the empty-room case, and nearest-by-RSSI is the proven heuristic for the remaining case. **Residual limitation:** while your AirPods are connected, a *nearer* stranger's AirPods advertisement could still be tracked — documented, hardened in Phase 8 | 2026-07-09 |
| **Active scanning** for the advertisement watcher | Ensures the full manufacturer payload (proximity message) is captured rather than only the base advertisement | 2026-07-09 |
| Battery shown per Apple's **10% granularity** with an explicit **"unknown"** for the sentinel/absent value and a charging indicator | The proximity message encodes battery in coarse steps with an "unknown" sentinel; presenting it honestly follows the constitution's honesty principle | 2026-07-09 |
| Battery/in-ear go **stale after a 30 s timeout** with no fresh advertisement → tray shows "unknown / out of range" | Advertisements stop on disconnect/out-of-range; a bounded timeout prevents showing a stale value as live (graceful degradation) | 2026-07-09 |
| The **Continuity byte-format** unit is research-intensive → split into `chore:research-continuity-parser` (proximity-message layout: battery nibbles, charging bits, in-ear/lid bits, model id) + an implementation issue depending on it | Contract pre-classifies AAP/Continuity byte-format work as research-intensive (≥3 source lookups); byte layout must be cross-checked across sources | 2026-07-09 |
| The **WinRT advertisement-watcher behaviour** unit is research-intensive → split into `chore:research-ble-watcher` (scanning mode, `0x004C` manufacturer-data filter, capability/permission needs on Win11, no-admin) + an implementation issue depending on it | Contract pre-classifies Windows BLE API behaviour confirmation as research-intensive | 2026-07-09 |
| The **Windows media-session** unit is research-intensive → split into `chore:research-media-control` (GSMTC pause/play, playback-state read, driver-free/no-admin) + an implementation issue depending on it | Contract pre-classifies Windows-audio API behaviour confirmation as research-intensive | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue
per step, grouped under this phase's milestone. This spec owns the design; the
issues own progress.

- Milestone: created on merge (one per this phase); depends on the Phase-1
  milestone.
- Issues: created from this spec once merged (one per implementable step).

Each issue references this spec path in its body.

## Verification

How Claude Code / the developer knows the whole spec is complete. This list
doubles as the human milestone-QA script; items no machine check covers are
verified by the human on real hardware.

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and unit tests all green.
- [ ] A **fake `IBleScanner`** feeds captured/synthetic `0x004C` advertisement
      byte fixtures and a unit test asserts the decoded left/right/case battery %,
      charging flags, and in-ear state (device-independent; Tier-1 test gate).
- [ ] A **fake `IMediaController`** unit test drives in-ear→out-of-ear→in-ear
      transitions and asserts pause-on-remove, resume-on-reinsert, and that media
      the user paused is **not** auto-resumed (device-independent; Tier-1 test gate).
- [ ] **Gating works** (device-independent unit test): with a fake
      `IConnectionMonitor` reporting an AirPods device **connected**, the
      strongest-RSSI `0x004C` advertisement drives battery + play/pause; with it
      reporting **no AirPods connected**, no live battery is shown and no play/pause
      fires even though `0x004C` advertisements are being fed in.
- [ ] The **`chore:research-continuity-parser` research comment** is posted and the
      parser's offsets/opcodes reflect its consensus (contract: research comment as
      QA artefact).
- [ ] The **`chore:research-ble-watcher` research comment** is posted and the
      `WinRtBleScanner` scanning mode + `0x004C` filter + capability handling reflect
      its consensus (contract: research comment as QA artefact).
- [ ] The **`chore:research-media-control` research comment** is posted and the
      `WindowsMediaController` reflects its consensus (contract: research comment as
      QA artefact).
- [ ] Company-id identification works **on the advertisement path**: a `0x004C`
      AirPods advertisement is picked up while non-Apple BLE advertisements are
      ignored. (This is **added** for telemetry; Phase-1's name-based connection
      detection is unchanged — the company id never enters the connection path.)
- [ ] **(Human QA gate)** On a real machine with real AirPods **connected**: the
      tray shows correct left/right bud + case battery with charging indicators,
      updating as the case/buds charge.
- [ ] **(Human QA gate)** Removing a bud pauses active media within a couple of
      seconds; re-inserting resumes it; media the user paused is not auto-resumed.
- [ ] **(Human QA gate)** With **no AirPods connected**, nearby AirPods
      advertisements do **not** show a live battery in the tray and do **not** drive
      play/pause (the connection gate holds).
- [ ] **(Human QA gate)** Taking AirPods out of range (or closing the case) makes
      the tray show "unknown / out of range" after the staleness timeout — no crash,
      no stale value shown as live.
- [ ] The process runs **without elevation** (`asInvoker`) — no admin prompt, no
      driver present.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Continuity proximity-message byte layout differs by model/firmware | Confirmed up front by `chore:research-continuity-parser` across ≥3 sources; parser isolated in Core with fixture-driven unit tests; broader model/firmware coverage is Phase 8. |
| WinRT advertisement watcher needs a capability or behaves differently across Win11 builds | `chore:research-ble-watcher` confirms scanning mode, filter, and capability needs; no admin is ever requested; behaviour is hidden behind `IBleScanner` with a fake for tests and smoke-tested on real hardware. |
| A stranger's AirPods drive the tray/play-pause (advertisement can't be cryptographically bound — Apple rotates random addresses) | Gate tracking on the Phase-1 connection state (only track while an AirPods device is connected), then pick the strongest-RSSI `0x004C` advertisement; residual crowded-room cross-talk (a *nearer* stranger's AirPods while yours are connected) is a documented limitation and multi-device disambiguation is deferred to Phase 8; covered by the gating unit test. |
| Auto-resume fights the user (resumes media they intentionally paused) | Engine only resumes when PodBridge recorded the pause; GSMTC playback-state read makes "paused-by-us" observable; covered by the play/pause unit test. |
| CI cannot exercise Bluetooth or media playback | CI runs Verify (build/format/unit via fakes) only; battery, in-ear, and play/pause behaviour are checked at the human QA gate on real hardware (contract QA-gate default = UI check / smoke test). |
| Media-session control resumes/pauses the wrong app when several are playing | Target the current GSMTC session; document the single-active-session assumption and confirm at the QA gate. |

## Decision log

- 2026-07-09: Spec drafted. Autonomous bulk planning — genuinely-open points were
  resolved with documented defaults (recorded in Prior decisions and surfaced in
  the plan's openDefaults) rather than left OPEN: media-control mechanism (GSMTC),
  resume policy (paused-by-us only), pause trigger (first bud out), multi-device
  disambiguation (strongest RSSI, single device), and the staleness timeout (30 s).
  These are the items a human may override at the spec-acceptance gate.
- 2026-07-09: Addressed the spec-review must-fix findings. (1) Removed the
  "replaces the Phase-1 name heuristic" conflation: company id `0x004C` lives on
  the BLE **advertisement** path and does not appear in the paired-device
  connection enumeration, so it cannot replace the connection-monitor's name
  match. Phase-1's name-based `IConnectionMonitor` is **unchanged**; company-id
  identification is **added** for advertisement telemetry only — fixed across the
  Outcome bullet, In-scope/Out-of-scope lines, the Prior-decision row, and the
  Verification item. (2) Defined the scanner↔connection-monitor integration:
  advertisement tracking, battery display, and play/pause are **gated on the
  Phase-1 connection state** (track only while an AirPods device is connected),
  with the residual crowded-room cross-talk stated as an explicit limitation
  (hardened in Phase 8). Added the gate as an Outcome, an In-scope item, a
  Prior-decision row, unit-test coverage, and Verification items (machine +
  human QA).
