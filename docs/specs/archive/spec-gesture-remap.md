# Spec: Gesture Remap (Phase 7)

> Created: 2026-07-09

Add opt-in Tier-2 stem/press **gesture remap** on top of the Phase-6 driver path:
let the user reassign the AirPods SingleClick / DoubleClick / ClickHold press
gestures via the AAP control channel, persist that choice, and **re-push it on
every reconnect** because Apple's firmware overwrites the configuration whenever
the buds reconnect. This spec carries no lifecycle state — acceptance is the spec
merged on the default branch with a milestone and issues, and all progress lives
in the GitHub issues and milestone. A completed spec is moved to
`docs/specs/archive/`.

## Outcome

- [ ] The clean-room `AapProtocol` module (added in Phase 6) gains **gesture-remap
      packet builders** for SingleClick / DoubleClick / ClickHold, each with a
      comment citing the documented opcode (gesture remap `0x14`/`0x15`/`0x16`),
      built from documented facts only — no GPL source or verbatim prose copied.
- [ ] A **device-independent unit test** (fake `IAapTransport`) asserts that each
      gesture/action assignment produces the exact expected byte sequence, and
      that per-bud (left/right) assignments target the correct bud — no physical
      AirPods required.
- [ ] The Core `IAapTransport` interface exposes a **Tier-2 (re)connect /
      handshake-complete event** (added this phase; raised by `DriverAapTransport`,
      fired by a fake in tests), and Core holds a persisted **gesture
      configuration** (per-bud action map) plus a **re-push policy** that re-sends
      the stored config whenever that event fires — covered by a
      **device-independent unit test** that fires the event on a fake
      `IAapTransport` and asserts the config was re-pushed.
- [ ] A **gesture-remap settings surface** lets the user assign an action to each
      supported gesture per bud; the assignment is written over the driver
      transport and persisted to the app's existing local settings store.
- [ ] **Graceful degradation (Tier-2 gate):** with the Phase-6 driver **absent**,
      the gesture UI is hidden/disabled, no gesture packet is ever attempted, the
      app does not crash, and the **Tier-1 suite still passes** with the driver
      uninstalled.
- [ ] **Honest signing surface (Tier-2 gate):** the gesture surface reuses the
      Phase-6 driver / test-mode / signing notice unchanged; no string claims a
      Microsoft-signed driver, and no new signing claim is introduced.
- [ ] **Verify stays green** (build + analyzers-as-errors + `dotnet format
      --verify-no-changes` + `dotnet test`).

## Scope

### In scope

- Gesture-remap packet builders in the existing `PodBridge.Core/AapProtocol`
  module for the three press gestures (SingleClick / DoubleClick / ClickHold),
  with per-bud (left/right) addressing where the model supports it.
- A **Tier-2 (re)connect / handshake-complete event on the Core `IAapTransport`
  interface** (the OS-free abstraction, not just the concrete
  `DriverAapTransport`): Phase 7 **adds** this event to `IAapTransport` so the
  OS-free re-push policy can subscribe to it and a fake `IAapTransport` can fire
  it in tests. `DriverAapTransport` (Phase 6, Windows) raises it from its
  **existing** L2CAP-connect + AAP-handshake path; no other Phase-6 code changes.
- A Core gesture-configuration model + persistence abstraction (`IGestureConfigStore`)
  and a **re-push-on-reconnect policy** that re-sends the stored config whenever
  the Core `IAapTransport` raises its Tier-2 (re)connect / handshake-complete
  event (Apple firmware overwrites the config on reconnect).
- A gesture-remap **settings UI** in `PodBridge.App`, gated on driver presence,
  writing via the existing `DriverAapTransport` (the composition-root
  `IAapTransport` implementation) and persisting to the app's local settings
  store.
- Device-independent unit tests for both the packet building and the re-push
  policy (fake `IAapTransport`).

### Out of scope

- The KMDF L2CAP driver itself, `DriverAapTransport`'s driver-side logic, the
  `AapProtocol` **write path / handshake**, ANC/Transparency/Adaptive switching,
  and the driver install / test-mode / signing UX — all **Phase 6** (consumed
  unchanged). **One carved-out exception:** Phase 7 adds a single Tier-2
  (re)connect / handshake-complete **event** to the Core `IAapTransport`
  interface (see In scope), and `DriverAapTransport` gains only the code to
  **raise** that event on its existing connect/handshake path — the write path
  and handshake logic themselves are untouched.
- Broadening supported **models / firmware**, firmware-fragility hardening, and
  Tier-2 diagnostics — **Phase 8**.
- Battery %, in-ear, and BLE-advertisement telemetry — **Phase 2**; codec
  transparency and the microphone-profile policy — **Phases 3–4**; the
  installer, winget, and the not-affiliated disclaimer surface — **Phase 5**;
  paired/connected detection and the tray shell — **Phase 1**.
- Conversation awareness (`0x28`/`0x4B`) and device rename (`0x1E`/`0x1A`) —
  not on the roadmap, explicitly not this phase.

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10, `Core` OS-free and clean-room, adapters in `Windows`, composition
  root in `App`, Apache-2.0, warnings-as-errors in Core, max 50-line functions).
- Component boundaries per `docs/architecture.md`, Tier-2 flow #4: `App` command
  → `Core AapProtocol` builds the packet → `DriverAapTransport` writes it over
  L2CAP → the AirPods echo confirms → `DeviceState` updated; **the driver is
  reached only through `DriverAapTransport`**, never directly from `App`/`Core`.
  This phase adds a `(re)connect / handshake-complete` event to the Core
  `IAapTransport` interface (implemented by `DriverAapTransport`, raised from its
  existing connect/handshake path); `docs/architecture.md` is updated to list it
  when implemented (living doc).
- **Tier-2 opt-in + graceful degradation (constitution):** every Tier-1 feature
  must still work and the Tier-1 test suite must pass with the driver
  uninstalled; the gesture feature is disabled when the driver is absent.
- **Clean-room protocol (constitution):** each gesture opcode/constant lives in
  `AapProtocol` with a comment citing the documented fact; no GPL source or
  verbatim doc prose copied into this Apache-2.0 tree.
- **Research-intensive:** the gesture packet byte-format (opcodes, gesture IDs,
  action-enum values, per-bud addressing) and the reconnect-overwrite behaviour
  require ≥ 3 external source lookups → split into a
  `chore:research-gesture-aap` issue whose research comment is the sole content
  authority for the implementation issue (contract §"Research-intensive issues").
- Verify = `powershell -NoProfile -File build/verify.ps1`; Tier-2 changes
  additionally require a **manual smoke test** on a machine with the Phase-6
  driver installed (constitution + workflow QA-gate rule — CI has no Bluetooth).

## Prior art

- [AAP / AACP protocol reverse-engineering](../prior-art.md#aap--aacp-protocol-reverse-engineering)
  — the definitive documented facts: gesture remap `0x14`/`0x15`/`0x16`,
  plaintext AAP control channel over L2CAP PSM `0x1001`. Reimplement clean-room
  from these facts only; note the firmware-fragility warning (spec pinned to
  AirPods Pro 2 USB-C fw 7A305) that Phase 8 owns.
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — confirms gesture writes need the Classic-L2CAP channel that only the
  Phase-6 kernel driver exposes; there is no user-mode path, so the whole
  feature is Tier-2.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — MagicPods' gesture customization is the UX bar to beat; AirPodsWindows
  proves open ANC on Windows but has **no gesture remap** (the genuine gap this
  phase fills). Patterns only — both are proprietary or GPL-3.0, no code reuse.
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — WinPods (MIT, C#/.NET 10) is the AAP-implementation reference and confirms
  the driver-free BLE tier vs KMDF-L2CAP tier split this phase's UI gate relies
  on.
- [Legal & licensing (not legal advice)](../prior-art.md#legal--licensing-not-legal-advice)
  — reimplement AAP from documented facts (interoperability-protected); use only
  the cleartext AAP control channel; the driver-signing reality (EV cert / test
  mode) inherited from Phase 6 stays honestly surfaced.

## Human prerequisites

- [ ] **none new for Phase 7.** No secret, account, or certificate is required to
      build, unit-test, or Verify this phase — all Core logic and the packet
      builders are device-independent. The Tier-2 **manual smoke test** depends
      on a machine that already has the **Phase-6** driver installed (test-signed,
      or EV-signed if that certificate was obtained); provisioning that driver /
      EV certificate is **Phase 6's** human prerequisite, not a new one here.

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| The gesture packet byte-format + reconnect-overwrite behaviour is **research-intensive** → split into `chore:research-gesture-aap` (research comment) + a `feat` implementation issue that depends on it and reads the comment as sole content authority | Contract pre-classifies any AAP byte-format work as research-intensive (≥ 3 source lookups: librepods `AAP Definitions.md`/`opcodes.md`/`control_commands.md`, its Wireshark dissector, WinPods AAP source) | 2026-07-09 |
| Gestures exposed = **SingleClick / DoubleClick / ClickHold** only, assigned **per bud (left/right)** where the model advertises it, falling back to a shared assignment otherwise | Matches the phase intent and the documented `0x14`/`0x15`/`0x16` gesture set; per-bud mirrors Apple's own model | 2026-07-09 |
| Re-push is triggered on a **Tier-2 (re)connect / handshake-complete event surfaced on the Core `IAapTransport` interface** — raised by `DriverAapTransport` on the L2CAP connect/handshake, fired by the fake transport in tests — not the Tier-1 BLE connection event | Apple overwrites the gesture config specifically after the L2CAP reconnect; the trigger must live on the OS-free `IAapTransport` abstraction so the Core re-push policy can subscribe to it and a fake can fire it device-independently — Core never sees the concrete `DriverAapTransport` (constitution: Core is OS-free, all OS access behind interfaces) | 2026-07-09 |
| Phase 7 **adds the (re)connect / handshake-complete event to `IAapTransport`** (in-scope Core interface addition), rather than assuming Phase 6 already exposes it | Phase 6 (ANC switching) had no re-push requirement, so it is not safe to assume the event already exists on the abstraction; re-push-on-reconnect is Phase 7's headline mechanism, so Phase 7 owns the event that drives it. `DriverAapTransport` raises it from its existing Phase-6 connect/handshake path (no write-path/handshake change); the Out-of-scope "consumed unchanged" note carves out this one addition | 2026-07-09 |
| The gesture config persists in the **app's existing local settings store** (the one Phase 6 uses), behind a Core `IGestureConfigStore` abstraction — no new file format | Keeps Core OS-free and testable; avoids a second settings surface; local-only per constitution | 2026-07-09 |
| The exposed **action set** is exactly what the research comment confirms is settable for the supported model — no invented actions; any action the connected model/firmware does not advertise is hidden | Honesty + firmware-fragility; avoids sending unsupported opcodes | 2026-07-09 |
| Write confirmation **reuses Phase 6's write+echo-confirm** pattern; a missing echo surfaces a non-fatal "couldn't apply" state with a single retry, no retry storm | Consistent Tier-2 behaviour; avoids hammering the L2CAP channel | 2026-07-09 |
| Phase 7 targets the **models the Phase-6 driver + AAP path already support** (Pro 2 USB-C pinned); unsupported models hide the feature; broad model/firmware coverage is **Phase 8** | Tight scope binding to the neighbour phases; firmware fragility is a known Phase-8 concern | 2026-07-09 |
| The gesture surface **reuses the Phase-6 signing / test-mode notice unchanged** and adds no new signing claim | Constitution "honest about signing"; Phase 6 owns the signing UX | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one
issue per step, grouped under this phase's milestone. This spec owns the design;
the issues own progress.

- Milestone: created on merge (one per this phase; carries `Depends on
  milestone: #<Phase-6 milestone>` in its description).
- Issues: created from this spec once merged (one per implementable step).

Each issue references this spec path in its body.

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      analyzers-as-errors, format check, and unit tests all green.
- [ ] The **gesture-remap research comment** (from `chore:research-gesture-aap`)
      is posted with ≥ 3 cross-checked sources and a consensus on opcodes,
      gesture IDs, action-enum values, per-bud addressing, and the
      reconnect-overwrite behaviour; its consensus is reflected in the
      implementation (contract: research comment is a QA artefact).
- [ ] A **fake `IAapTransport`** unit test asserts each gesture/action assignment
      builds the exact expected byte sequence and that left vs right bud is
      addressed correctly — device-independent.
- [ ] A **fake-transport** unit test raises the `IAapTransport` (re)connect /
      handshake-complete event and asserts the stored gesture config is
      **re-pushed** — device-independent (the event is on the Core abstraction,
      so no driver is involved).
- [ ] With the **driver absent**, the gesture UI is hidden/disabled, no gesture
      packet is attempted, the app does not crash, and the **Tier-1 test suite
      passes** with the driver uninstalled (Tier-2 graceful-degradation gate).
- [ ] Review confirms the gesture surface reuses the **Phase-6 signing / test-mode
      notice** and adds no string claiming a signed driver (honest-signing gate).
- [ ] Review confirms every gesture opcode/constant in `AapProtocol` carries a
      **citation comment** and that no GPL source or verbatim prose was copied
      (clean-room gate).
- [ ] **(Human QA gate — manual smoke test, real hardware + Phase-6 driver
      installed):** assigning an action to a gesture takes effect on the AirPods;
      after physically disconnecting and reconnecting the buds, the assignment is
      **automatically re-applied** (re-push confirmed on real hardware).

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Gesture packet byte-format wrong or firmware-specific | Confirmed up front by the research issue across ≥ 3 sources; device-independent byte-level unit tests; verified on real hardware at the QA gate. |
| Apple firmware overwrites gesture config on reconnect (the core problem) | Explicit re-push policy triggered by the `IAapTransport` (re)connect / handshake-complete event, unit-tested by firing that event on a fake transport and confirmed on real hardware at the QA gate. |
| Firmware fragility (prior-art: pinned to Pro 2 USB-C fw 7A305) | Feature hidden on unsupported models/firmware; a missing echo surfaces a non-fatal "couldn't apply" state; broad model/firmware coverage is Phase 8. |
| Sending an unsupported action opcode to the buds | Only actions the research comment confirms settable for the connected model are exposed; unknown actions hidden. |
| Tier-1 regressions from Tier-2 wiring | Constitution graceful-degradation gate: Tier-1 suite must pass with the driver uninstalled; gesture code never runs when `DriverAapTransport` is absent. |
| Adding an event to `IAapTransport` ripples into Phase-6 code | The addition is a single event on the Core interface; `DriverAapTransport` raises it from its existing connect/handshake path — no change to the write path or handshake logic; `docs/architecture.md` records the addition (living doc). |
| CI cannot exercise Bluetooth / the driver | CI runs Verify (build/format/unit) only; gesture behaviour and re-push are checked at the human QA gate on a machine with the Phase-6 driver installed. |

## Decision log

- 2026-07-09: Spec drafted (autonomous bulk planning). Genuinely-open points were
  settled with documented defaults rather than left open — see Prior decisions
  (gesture set + per-bud addressing, re-push trigger channel, persistence store,
  exposed-action policy, write-confirmation/retry, model scope). The gesture
  byte-format unit of work is committed to the research-intensive split
  (`chore:research-gesture-aap` + a dependent implementation issue), with the
  research comment named as a QA artefact.
- 2026-07-09: Revised to resolve the spec-review must-fix — pinned the re-push
  trigger to a **Tier-2 (re)connect / handshake-complete event on the Core
  `IAapTransport` interface** (not the concrete `DriverAapTransport`), so the
  OS-free re-push policy can subscribe and a fake transport can fire it
  device-independently. Recorded that adding this event to `IAapTransport` is an
  **in-scope Core interface addition** for Phase 7 (Phase 6, being ANC-only, had
  no re-push need, so the event is not assumed pre-existing), and carved that one
  addition out of the Out-of-scope "Phase 6 consumed unchanged" note while the
  `AapProtocol` write path / handshake stay unchanged. Added the interface event
  to the Outcome, In-scope, Constraints (living-doc `architecture.md` update),
  Verification, and Risks sections for consistency.
