# Spec: Model & Firmware Coverage / Hardening (Phase 8)

> Created: 2026-07-09

Broaden PodBridge from a single-model tool to the full AirPods line (AirPods 2,
3, Pro, Pro 2, Pro 3, Max), make it resilient to Apple's firmware fragility via a
Core model registry and a (model, firmware-major) capability matrix that gates
every feature honestly, add local-only diagnostics/logging for bug reports, and
run an app-wide hardening pass over the prior phases' flows. This spec carries no
lifecycle state — acceptance is the spec merged on the default branch with a
milestone and issues, and all progress lives in the GitHub issues and milestone.
A completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] PodBridge identifies each supported model (AirPods 2, 3, Pro, Pro 2, Pro 3,
      Max) from the Apple-Continuity advertisement via a Core model registry
      (`IModelRegistry`), exposing a strongly-typed `AirPodsModel`; an
      unrecognised Apple audio device degrades to a labelled **"Unknown AirPods"**
      generic mode (best-effort battery/ear, model-specific features disabled) and
      **never crashes**.
- [ ] A Core capability matrix (`ICapabilityProvider`) keyed by **(model,
      firmware-major)** decides which features are offered. **Tier-1** features
      gate on the **BLE-derived model axis only** (e.g. AirPods Max has no case,
      so case battery is hidden) — **never on firmware** — so Tier-1 keeps working
      with the driver absent. The **firmware-major** axis is readable only via the
      Tier-2 driver and refines **Tier-2** gating only. Tier-2 features
      (ANC/Transparency/Adaptive, gesture remap, conversation awareness) are gated
      on **both** driver presence **and** the (model, firmware-major) capability,
      and are hidden/disabled **with an honest reason string** when unsupported —
      never silently absent, never falsely claimed.
- [ ] Firmware-major is read via a firmware-version AAP command through the
      existing `IAapTransport` when the Tier-2 driver is present **and such a read
      exists on the cleartext AAP channel** (to be confirmed by
      `chore:research-firmware-capabilities` — prior-art documents no such opcode).
      When firmware-major is **unreadable** (driver absent, read fails, or no such
      opcode exists), a **known model falls back to its Phase-6/7 model-level
      Tier-2 capability** — it does **not** regress ANC/gestures that Phases 6/7
      already ship with the driver present. Only genuinely firmware-varying
      *refinements* on a known model fall back **conservatively to "assume
      unsupported"** (graceful degradation, constitution). **If research confirms
      no firmware-version read exists, the matrix degrades to model-only** (the
      firmware axis is dropped and Tier-2 gates on model + driver presence alone).
- [ ] A **local diagnostics snapshot** (model, firmware-major, negotiated codec,
      tier, driver presence + honest signing/test-mode status, capability matrix,
      recent BLE parse results) can be exported from the tray to a **local file +
      clipboard**; it contains **no secrets**, masks the Bluetooth device address,
      and makes **no network call** (local-only).
- [ ] Structured local logging (`Microsoft.Extensions.Logging`, a **rolling,
      size/age-capped local file** sink, Information default, Debug opt-in via a
      tray toggle) is wired across Core/Windows/App with **no network sink**.
- [ ] The Continuity parser and model registry tolerate **malformed / truncated /
      unknown** payloads without throwing, proven by **device-independent
      fuzz/property unit tests** (constitution Tier-1 test gate).
- [ ] An app-wide **hardening pass** covers the prior-phase flows (BLE-watcher
      restart on radio toggle, audio-policy restore on failure/crash,
      driver-absence, unknown-model) — each with a device-independent test or a
      named QA-gate scenario.
- [ ] **Verify stays green**; `docs/architecture.md` is updated to list the new
      Core types (`IModelRegistry`, `ICapabilityProvider`, diagnostics/logging
      boundary).

## Scope

### In scope

- **Model coverage:** a Core model registry mapping Apple-Continuity model
  identifiers to `AirPodsModel` for AirPods 2/3/Pro/Pro 2/Pro 3/Max, plus a
  per-model shape (dual-bud vs single, case present, in-ear supported — AirPods
  Max has no case and different ear handling), and the "Unknown AirPods" fallback.
- **Firmware fragility:** if a firmware-version read exists on the cleartext AAP
  channel (to be confirmed by `chore:research-firmware-capabilities` — prior-art
  documents no such opcode), reading firmware-major via that AAP command over the
  Tier-2 transport, plus a static, in-repo **capability matrix** keyed by
  **(model, firmware-major)**. **Tier-1** features gate on the **BLE-derived model
  axis only** (never on firmware, so Tier-1 works with the driver absent); the
  **firmware-major** axis refines **Tier-2** gating only. When firmware-major is
  unreadable, a known model falls back to its **Phase-6/7 model-level Tier-2
  capability** (not a blanket "assume unsupported" that would regress shipped
  ANC/gestures); only genuinely firmware-varying refinements fall back
  conservatively. If no firmware read exists, the matrix degrades to
  **model-only**.
- **Diagnostics:** a Core `DiagnosticsSnapshot` model + a tray "Export
  diagnostics" action writing a local, secret-free, address-masked file + copying
  to clipboard.
- **Logging:** `Microsoft.Extensions.Logging` wiring, rolling capped local file
  sink, Debug opt-in toggle.
- **Hardening pass:** resilience of the BLE watcher, audio-policy restore,
  driver-absence and unknown-model paths from Phases 2–7, plus parser fuzz tests.
- `docs/architecture.md` update for the new Core types (living doc).

### Out of scope

- The **initial** WinRT BLE scanner, Continuity parser, and the first
  company-id/name model identification — **Phase 2** (this phase broadens the
  registry it introduced, it does not create it).
- Negotiated-codec **detection** itself — **Phase 3** (Phase 8 only *reports* the
  Phase-3 result inside the diagnostics snapshot).
- The microphone-profile policy engine — **Phase 4** (Phase 8 hardens its
  restore-on-failure path, it does not add modes).
- Installer, MSIX, winget, disclaimer/About surface — **Phase 5**.
- Creating the **KMDF L2CAP driver, AAP write path, and the driver
  signing/test-mode/EV-cert UX** — **Phase 6** (Phase 8 adds **no new kernel
  component** and reopens **no** signing decision; it reads firmware over the
  Phase-6 transport and reports its signing status honestly).
- The **gesture-remap feature** and re-push-on-reconnect — **Phase 7** (Phase 8
  only *gates* gesture remap in the capability matrix).
- **Ongoing hardening beyond this milestone** — handled as `track:adhoc` issues,
  not by reopening this spec (see Prior decisions: full-spec, not living-spec).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10, `Core` OS-free with all OS access behind interfaces, Apache-2.0
  clean-room, nullable + warnings-as-errors in Core, max 50-line functions).
- Component boundaries per `docs/architecture.md`: model registry + capability
  matrix + diagnostics model are **Core** logic; the firmware read (if it exists)
  rides the existing `IAapTransport` (Tier-2); the diagnostics file writer and log
  file sink are **`PodBridge.Windows`** adapters; the tray "Export diagnostics"
  and Debug toggle are **`PodBridge.App`**.
- **Clean-room protocol (constitution):** the new firmware-version read AAP
  opcode/constant — if one is added at all — lives in the **single `AapProtocol`
  module** with a comment citing the documented fact (constitution: "Every AAP
  opcode/constant lives in one `AapProtocol` module, each with a comment citing
  the documented fact"). Every model identifier and firmware/capability constant
  likewise carries a citing comment. All are reimplemented from the research
  comments (which draw on `docs/prior-art.md` facts) — no GPL source or verbatim
  prose copied. If research confirms no firmware-version read exists on the
  cleartext AAP channel, **no read opcode is added**.
- **Graceful degradation (constitution):** every Tier-1 feature works with the
  driver absent and with an unknown model, and **Tier-1 gating never consults the
  firmware axis** (which is Tier-2-only); the Tier-1 test suite passes with the
  driver uninstalled.
- **Tier-2 is opt-in and honest (constitution):** capability gating never claims a
  feature works without the driver, and diagnostics reports the driver's
  signing/test-mode status truthfully (carries forward Phase-6 signing honesty).
  A firmware-unreadable state never silently gates OFF a Tier-2 feature the model
  supports at Phase-6/7 level — it falls back to that model-level capability.
- **Local-only (constitution):** diagnostics and logging write local files only;
  **no network sink, no telemetry, no secrets** — the Bluetooth address is masked
  in exported diagnostics.
- Verify = `powershell -NoProfile -File build/verify.ps1`; it must stay green, and
  the parser fuzz tests run inside it.

## Prior art

- [AAP / AACP protocol reverse-engineering](../prior-art.md#aap--aacp-protocol-reverse-engineering)
  — the source of the Continuity model identifiers and the firmware-fragility
  problem this phase addresses: the librepods spec is pinned to AirPods Pro 2
  USB-C **fw 7A305**, so opcodes/behaviour vary by firmware. Note the documented
  opcode set covers battery/in-ear/ANC/gesture/conversation-awareness/rename but
  **no firmware-version read** — `chore:research-firmware-capabilities` must
  confirm whether such a read exists on the cleartext channel. Reimplement the
  model and capability facts clean-room; drives both research issues.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — MagicPods is the **model-coverage bar** to match; CAPoD/AirStatus/OpenPods are
  references for **per-model BLE-advertisement battery decode** across the AirPods
  line (patterns/facts only — GPL-family, no code copied).
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — fixes the Tier-1/Tier-2 line for this phase: model identification + battery
  come from the **driver-free BLE advertisement** (Tier 1); the firmware read is
  **Tier-2** over the KMDF L2CAP transport and must degrade when the driver is
  absent.
- [Windows audio codec & quality](../prior-art.md#windows-audio-codec--quality)
  — the diagnostics snapshot reports the **negotiated codec (AAC vs SBC)** from
  Phase 3; AAC availability is model/radio-dependent, so the snapshot must state
  it honestly (never claim Apple-parity).

## Human prerequisites

- [ ] **none.** Phase 8 introduces **no new kernel component and no new signing
      artefact**, so the Phase-6 EV-cert / driver-signing question is **not**
      reopened here. Tier-2 capability negotiation is exercised at the human QA
      gate on a machine that already has the **Phase-6 test-signed driver**
      installed plus real AirPods — that is hardware at the QA gate, not a
      provisioning secret. No accounts, tokens, or certs are required to plan or
      merge this phase.

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Phase 8 ships as a **full-spec** milestone with a closeable done-criteria (model matrix + capability negotiation + diagnostics + one hardening pass), **not** a perpetual living-spec | The roadmap needs a closeable final milestone; "ongoing hardening" beyond this phase is handled by `track:adhoc` issues per the workflow contract, not by reopening this spec | 2026-07-09 |
| Supported model set = the **vision's six** (AirPods 2, 3, Pro, Pro 2, Pro 3, Max); any other Apple audio device degrades to a labelled **"Unknown AirPods"** generic mode (best-effort battery/ear, model-specific features off) | The vision names exactly these six; a graceful generic fallback satisfies the constitution's graceful-degradation principle and avoids crashes on new/unknown hardware | 2026-07-09 |
| Firmware capability is a **static, in-repo matrix keyed by (model, firmware-major)** derived from the research comment, **not** a live per-opcode runtime probe | Deterministic and device-independently testable (a live probe cannot be unit-tested with fakes and risks provoking firmware-fragile behaviour) | 2026-07-09 |
| **Tier-1 features gate on the BLE-derived model axis only** (never on firmware); the **firmware-major axis refines Tier-2 gating only** | Firmware-major is readable only via the Tier-2 driver; gating a Tier-1 feature on firmware would break the constitution's graceful-degradation / Tier-1-independence (Tier-1 must work with the driver absent). "AirPods Max has no case" is a model-axis fact, not a firmware fact | 2026-07-09 |
| When firmware-major is **unreadable** (driver absent, read fails, or no such opcode), a known model falls back to its **Phase-6/7 model-level Tier-2 capability**; only genuinely firmware-varying *refinements* fall back to **"assume unsupported"** | A blanket "assume unsupported" on any firmware-read failure would silently gate OFF ANC/gestures that Phases 6/7 already ship working with the driver present — an honest-but-wrong regression; the model-level fallback keeps shipped features on and remains conservative only where firmware genuinely changes behaviour | 2026-07-09 |
| `chore:research-firmware-capabilities` **first confirms whether a firmware-version read exists on the cleartext AAP channel** before the matrix keys on firmware | prior-art.md/librepods documents battery/in-ear/ANC/gesture/rename opcodes but **no firmware-version read opcode**; if none exists, the (model, firmware-major) matrix **degrades to model-only** and the firmware-gating Outcome says so — no read opcode is added | 2026-07-09 |
| Tier-2 features gate on **driver presence AND capability**, and show an **honest reason** when off ("requires the optional driver" / "not supported on this firmware") | Constitution: Tier-2 is opt-in, graceful when the driver is absent, and honest; never silently missing, never falsely claimed | 2026-07-09 |
| Diagnostics = a tray **"Export diagnostics"** action producing a human-readable local **file + clipboard** copy; **no auto-upload**, Bluetooth address masked, no secrets | Local-only constitution + real bug-report ergonomics; masking the stable device address avoids leaking a durable identifier in a shared file | 2026-07-09 |
| Logging = **`Microsoft.Extensions.Logging`** with a **rolling local file sink capped at ~10 MB / 7 days**, Information default, **Debug opt-in via a tray toggle**, no network sink | Standard .NET logging fits the generic host from Phase 1; the cap and local-only sink respect the no-telemetry / no-secrets rules; Debug opt-in keeps normal runs quiet | 2026-07-09 |
| Model identification is **research-intensive** → split `chore:research-model-ids` (Continuity model-byte table across AirPods 2/3/Pro/Pro 2/Pro 3/Max) + an implementation issue depending on it | Contract pre-classifies AAP/Continuity byte-format work as research-intensive (≥3 sources: librepods, CAPoD, AirStatus/OpenPods) | 2026-07-09 |
| Firmware capability is **research-intensive** → split `chore:research-firmware-capabilities` (firmware-read existence + read command + per-(model, firmware-major) feature matrix) + an implementation issue depending on it | Prior-art flags the AAP spec as firmware-fragile (pinned to one fw) and documents no firmware-read opcode; cross-source consensus is required before coding the matrix | 2026-07-09 |
| Firmware read (if any) rides the **existing `IAapTransport`** (Tier-2), not a new transport; its opcode lives in the single `AapProtocol` module | Architecture: anything needing L2CAP goes through `DriverAapTransport`; constitution: every AAP opcode lives in one `AapProtocol` module with a citing comment | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue
per step, grouped under this phase's milestone. This spec owns the design; the
issues own progress. The milestone depends on the Phase-7 milestone (`Depends on
milestone:` wired outside this spec).

- Milestone: created on merge (one per this phase)
- Issues: created from this spec once merged (one per implementable step)

Each issue references this spec path (`docs/specs/spec-model-coverage-hardening.md`).

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and all unit tests (including the new fuzz tests) green.
- [ ] **Model-registry unit test (device-independent):** fixture Continuity
      payloads for AirPods 2, 3, Pro, Pro 2, Pro 3, Max each identify to the
      correct `AirPodsModel`; an unknown identifier yields "Unknown AirPods"
      (Tier-1 test gate).
- [ ] **Tier-1 model-axis gating unit test (device-independent):** a Tier-1
      feature (e.g. case battery) is gated by the **model axis only** — AirPods Max
      (no case) hides it, dual-bud models show it — and the gate **never consults
      the firmware axis or the driver**, so it holds identically with the driver
      absent (Tier-1 independence).
- [ ] **Capability-gating unit test (device-independent):** a fake
      `ICapabilityProvider` + fake `IAapTransport` assert that Tier-2 features are
      **off** with **"requires the optional driver"** when the driver is absent;
      that when the driver is present but firmware-major is **unreadable** they
      fall back to the **model-level Tier-2 capability** (a model that ships
      ANC/gestures at Phase-6/7 level stays **on**, not regressed); that a
      firmware-major explicitly marked unsupported gates **off** with **"not
      supported on this firmware"**; and that the on-state holds only when driver
      presence and capability both hold — each with the expected honest reason
      string (Tier-1 test gate).
- [ ] **Parser fuzz/property test (device-independent):** truncated, over-long,
      and random Continuity payloads never throw and never mis-identify a known
      model's fixture (Tier-1 hardening gate).
- [ ] **Diagnostics-snapshot unit test (device-independent):** a snapshot built
      from a fake device state is deterministic, contains the masked address (no
      full address), contains no secret, and the writer performs no network call.
- [ ] The **`chore:research-model-ids` research comment** ("Research: AirPods
      Continuity model identifiers") is posted and its consensus table is reflected
      in the model registry (research-comment as QA artefact).
- [ ] The **`chore:research-firmware-capabilities` research comment** ("Research:
      AirPods firmware capability matrix") is posted; it states **whether a
      firmware-version read exists on the cleartext AAP channel** (and its opcode
      if so, or that the matrix degrades to model-only if not) and the
      per-(model, firmware-major) matrix, and its consensus is reflected in the
      capability matrix (research-comment as QA artefact).
- [ ] Tray **"Export diagnostics"** writes a local file + copies to clipboard; the
      file opens as readable text and shows model, firmware-major, codec, tier,
      driver presence + honest signing/test-mode status, and the capability matrix.
- [ ] The **Debug logging toggle** raises verbosity to the local file only; the
      log file rolls and is capped (no unbounded growth); no network sink exists.
- [ ] **Graceful degradation:** with the driver **uninstalled**, all Tier-1
      features work (model-axis gating unchanged), Tier-2 features show their
      honest "requires the optional driver" reason, and the Tier-1 test suite
      passes.
- [ ] **(Human QA gate)** On real hardware with real AirPods and the Phase-6
      test-signed driver installed: at least two different AirPods models are
      correctly identified; ANC/gesture availability matches the capability matrix
      for the connected model (and firmware-major if a read exists); with the
      driver present but firmware unreadable, previously-working ANC/gestures for
      that model are **not** regressed; radio toggle triggers a clean BLE-watcher
      restart; a diagnostics export produced on the machine is complete and
      address-masked.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Continuity model-byte tables disagree across sources / omit newer models (Pro 3) | `chore:research-model-ids` cross-checks ≥3 sources and documents disputes → majority; unknown identifiers degrade to "Unknown AirPods" rather than fail. |
| The cleartext AAP channel may have **no firmware-version read opcode** (librepods documents none) | `chore:research-firmware-capabilities` confirms existence **first**; if none, the matrix **degrades to model-only** (Tier-2 gates on model + driver presence), no read opcode is added, and the firmware-gating Outcome is dropped — no feature regresses. |
| A firmware-unreadable state silently gates OFF Tier-2 features Phases 6/7 already ship | Firmware-unreadable falls back to the **model-level Tier-2 capability**, not a blanket "assume unsupported"; only genuinely firmware-varying *refinements* fall back conservatively; the capability-gating test asserts a Phase-6/7-supported model stays on when firmware is unreadable. |
| Firmware behaviour drifts (librepods pinned to fw 7A305) and a matrix entry is wrong | Static matrix keyed by (model, firmware-major); a wrong "supported" only surfaces on real hardware and is caught at the QA gate; capability changes are cheap data edits, not code. |
| No CI hardware to exercise multi-model / firmware / Tier-2 | Device-independent fakes cover the registry, model-axis + firmware gating, parser, and diagnostics logic; real-hardware behaviour is the QA-gate script above (contract QA-gate default = smoke test; Tier-2 requires the driver installed). |
| Diagnostics export leaks a durable device identifier | Bluetooth address is masked and a unit test asserts no full address and no secret is present; no auto-upload (local file + clipboard only). |
| Log file grows unbounded or a sink reaches the network | Rolling sink capped at ~10 MB / 7 days; a review + test confirms the only sink is the local file (local-only constitution). |
| Hardening pass creeps into new features / prior-phase redesign | Scope fixed to resilience of existing flows + the model/capability/diagnostics additions; anything larger becomes a `track:adhoc` issue, not this milestone. |

## Decision log

- 2026-07-09: Spec drafted (autonomous bulk planning). All design forks settled
  with documented defaults (see Prior decisions + the milestone's openDefaults):
  full-spec (not living-spec) track; six-model set + "Unknown AirPods" fallback;
  static (model, firmware-major) capability matrix; local file + clipboard
  diagnostics with address masking; `Microsoft.Extensions.Logging` rolling capped
  local sink with Debug opt-in. Two research-intensive units split out
  (`chore:research-model-ids`, `chore:research-firmware-capabilities`), each with
  its research comment named as a QA artefact. Human prerequisites: none (no new
  kernel component; Phase-6 signing not reopened).
- 2026-07-09: Addressed the spec-review must-fix items. Pinned the capability-
  matrix key to **(model, firmware-major)** across all sections. Disambiguated the
  gating axes: **Tier-1 gates on the BLE-derived model axis only, never on
  firmware** (Tier-1 independence); **firmware-major refines Tier-2 only**. Fixed
  the firmware-unreadable default so it falls back to the **Phase-6/7 model-level
  Tier-2 capability** instead of a blanket "assume unsupported" that would regress
  shipped ANC/gestures. Made `chore:research-firmware-capabilities` first confirm
  whether a firmware-version read exists on the cleartext AAP channel (prior-art
  documents none); if not, the matrix **degrades to model-only**. Placed the new
  firmware-read AAP opcode (if any) in the single `AapProtocol` module with a
  citing comment per the constitution.