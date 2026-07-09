# Spec: Advanced tier — KMDF L2CAP driver + AAP writes (Phase 6)

> Created: 2026-07-09

Deliver the OPTIONAL Tier-2 path: a C/KMDF L2CAP bridge driver in `driver/PodBridgeAAP` that opens the AAP control channel (PSM 0x1001) to connected AirPods, a clean-room `AapProtocol` noise-control module plus an `IAapTransport` implementation, and tray UI to switch noise control (Off / ANC / Transparency / Adaptive). The whole tier is explicitly opt-in, degrades gracefully to a working Tier-1 app when the driver is absent, and is honest about the Windows driver-signing reality (test-signing mode + test-cert trust vs Microsoft attestation). This spec carries no lifecycle state — acceptance is the spec merged on the default branch with a milestone and issues, and all progress lives in the GitHub issues and milestone. A completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] An OPTIONAL C/KMDF L2CAP bridge driver in `driver/PodBridgeAAP` opens AAP
      **PSM 0x1001** to a connected AirPods device and exposes a user-mode WDF
      **device interface**; it ships and installs **separately** from the app
      (its own INF + `pnputil`) and is never bundled in the Phase-5 MSIX.
- [ ] `PodBridge.Core` gains a clean-room `AapProtocol` noise-control module and
      the `IAapTransport` interface: it builds/parses the plaintext handshake,
      the notification-register packet, and the ANC/Transparency/Adaptive
      **set + read** packets, with each opcode/constant commented citing its
      documented fact (per the clean-room principle).
- [ ] `DriverAapTransport` (`PodBridge.Windows`) implements `IAapTransport` over
      the driver's device interface; it **probes for the driver and reports
      `Unavailable` when it is absent**, and is the ONLY component that talks to
      the driver (`Core`/`App` never do).
- [ ] The tray UI offers **noise-control switching (Off / ANC / Transparency /
      Adaptive)** for a supported connected model; a change is applied
      optimistically and **confirmed by the AirPods echo notification**, reverting
      the UI on timeout with a transient error.
- [ ] With the driver **absent**, every Tier-1 feature still works, the Tier-1
      test suite passes, and the noise-control UI is disabled with an honest
      explanation + an explicit opt-in affordance — **no crash, no elevation**.
- [ ] The advanced tier is **explicitly opt-in**: enabling it is a separate,
      user-triggered, **elevated** install step — driver INF via `pnputil`
      **plus importing the self-signed test certificate into the machine's
      Trusted Root CA / Trusted Publishers stores** (both required for an x64
      test-signed driver to load); the app keeps its `asInvoker` manifest and
      **never enables test-signing** (`bcdedit`) on the user's behalf.
- [ ] The UX and docs are **honest about driver signing**: they state **both**
      requirements to load the test-signed driver on x64 — **enabling
      test-signing mode (`bcdedit`) AND trusting the self-signed test certificate
      (import into Trusted Root CA / Trusted Publishers)** — and their machine-wide
      security trade-off, and make no claim of a Microsoft-signed /
      production-attested driver.
- [ ] **Device-independent unit tests (fake `IAapTransport`)** cover the ANC
      set/read packet encode+parse, the echo-confirm / timeout-revert logic, and
      the driver-absent graceful-degradation path (constitution Tier-1 test gate
      applied to the testable Core logic of this tier).

## Scope

### In scope

- `driver/PodBridgeAAP`: a C / KMDF L2CAP profile driver opening Classic-L2CAP
  PSM 0x1001, plus its INF and a user-triggered `pnputil` install flow.
- `PodBridge.Core`: the `IAapTransport` interface and a clean-room `AapProtocol`
  module limited to the **noise-control** commands — plaintext handshake,
  notification-register, and ANC/Transparency/Adaptive set+read — and the
  `NoiseControl` extension of `DeviceState`.
- `PodBridge.Windows`: `DriverAapTransport` implementing `IAapTransport` over the
  driver's device interface (open / send / receive-notification), with presence
  probing and graceful `Unavailable`.
- `PodBridge.App`: the noise-control tray submenu, the opt-in "enable advanced
  tier" affordance, the driver-absent / test-signing honesty UX, and the
  composition-root wiring of `DriverAapTransport`.
- Honest driver-signing documentation and in-app copy (test-signing mode **and**
  test-certificate trust + their combined security trade-off).

### Out of scope

- **Gesture / stem-press remap** (AAP 0x14/0x15/0x16) and re-push on reconnect —
  **Phase 7** (it reuses this phase's `IAapTransport`/driver but is a separate
  feature).
- Conversation awareness (0x28/0x4B), device rename (0x1E/0x1A), and any AAP
  command beyond noise control — later phases only.
- **Broad model / firmware coverage, the full Adaptive-support matrix, firmware-
  fragility handling, and diagnostics** — **Phase 8** (this phase targets the
  AirPods Pro 2 reference model and gates Adaptive on what the connected model
  reports).
- Battery %, in-ear detection, codec, and mic-profile — those stay the Tier-1
  BLE/audio paths from **Phases 2–4**; this phase does **not** move any of them
  onto the L2CAP channel.
- The **app** installer (MSIX + winget) and the not-affiliated / About surface —
  **Phase 5** (the driver-free MVP); only the *driver's own* INF installer is in
  scope here.
- A **Microsoft-attestation-signed public release** (EV cert + Partner Center) —
  out of scope; Phase 6 ships **test-signed** and documents the attestation path
  as a deferred, human-provisioned prerequisite.

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`:
  the driver is **C / KMDF (WDK)** — the only way to open a custom Classic-L2CAP
  PSM on Windows; `PodBridge.Core` stays OS-free (the `AapProtocol` module and
  `IAapTransport` only — no P/Invoke, no WinRT); `DriverAapTransport` lives in
  `PodBridge.Windows`; `App` wires it at the composition root only.
- This phase implements exactly **Key flow 4** of `docs/architecture.md`
  (noise-control Tier-2): `App` command → Core `AapProtocol` packet →
  `DriverAapTransport` write over L2CAP → AirPods echo → `DeviceState` update;
  driver absent → feature disabled. `docs/architecture.md` is updated (living
  doc) to mark `AapProtocol`/`IAapTransport`/`DriverAapTransport` as implemented.
- **Clean-room** (constitution): every AAP opcode/constant lives in one
  `AapProtocol` module with a comment citing the documented fact (PSM, opcode,
  value); no source or verbatim doc prose is copied from the GPL-3.0 librepods /
  AirPodsDesktop / AirPodsWindows projects — facts only.
- **No defeating MagicPairing** (constitution Don'ts): use only the cleartext AAP
  control channel (the 16-byte plaintext handshake); no crypto is broken.
- **Opt-in invasiveness** (constitution Don'ts): a kernel driver is installed and
  elevation is requested **only** on explicit user opt-in; MSIX cannot cleanly
  bundle a kernel driver, so the driver is its own INF + `pnputil` installer.
- **Graceful degradation** (constitution): with the driver absent, every Tier-1
  feature works and the Tier-1 test suite passes; verified by running tests with
  the driver uninstalled.
- The app **never runs `bcdedit`** (it is on the `.claude/settings.json` deny
  list and is a machine-wide security change): enabling test-signing mode is a
  manual user action the UX documents. Loading a self-signed test-signed KMDF
  driver on x64 **additionally** requires the test certificate to be trusted —
  imported into the machine's **Trusted Root CA / Trusted Publishers** stores —
  or the driver will not load even with test-signing on; the opt-in **elevated**
  install performs this cert import (scoped to the explicit opt-in, never
  silently), and the UX/docs state **both** requirements. Neither `bcdedit` nor
  the cert import happens without explicit user opt-in.
- Managed **Verify** (`powershell -NoProfile -File build/verify.ps1`) is unchanged
  and remains the per-PR gate for the C# projects. The KMDF driver builds
  separately with the WDK/EWDK; per `docs/workflow.md` a Tier-2 driver change
  **always** requires a manual smoke test (CI cannot exercise Bluetooth).

## Prior art

- [AAP / AACP protocol reverse-engineering](../prior-art.md#aap--aacp-protocol-reverse-engineering)
  — the definitive AAP facts to reimplement clean-room: PSM 0x1001, the 16-byte
  plaintext handshake, the notification-register packet, and ANC/Transparency/
  Adaptive read+set via 0x09/0x0D (Off=01 / ANC=02 / Transparency=03 /
  Adaptive=04). GPL-3.0 — facts only, firmware-fragile (pinned to Pro 2 USB-C).
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — confirms user mode gets RFCOMM + BLE/GATT only; a custom Classic-L2CAP PSM
  needs a KMDF profile driver (`BRB_L2CA_OPEN_CHANNEL`). The changcheng967/WinPods
  `driver/WinPodsAAP/` KMDF bridge is the clearest open blueprint (MIT), unverified
  on real hardware — reference, don't depend.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — MagicPods' `MagicAAP` kernel driver is direct proof the L2CAP tier needs a
  driver, and proof of the signing reality (even paid MagicPods ships unsigned /
  test-mode; its cross-signed build broke under the April-2026 policy).
  AirPodsWindows proves open-source ANC control is possible when the driver is
  delegated. Copy no proprietary/GPL code.
- [Legal & licensing (not legal advice)](../prior-art.md#legal--licensing-not-legal-advice)
  — the signing economics behind the honesty gate: a Microsoft-signed kernel
  driver needs an EV cert (~$250–560/yr) + Partner Center friction; clean-room
  reimplementation is interoperability-protected; keep Apache-2.0 (no GPL fork).
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — a separate C/C++ KMDF driver is the accepted shape for the optional tier
  only; the managed core stays C#/.NET.

## Human prerequisites

- [ ] **none** for this phase's default deliverable — the opt-in driver is
      **test-signed** with a locally-generated self-signed test certificate
      (`MakeCert`/`SignTool`, no purchase, no account). The end user enabling
      test-signing mode **and** trusting the test certificate on their own
      machine are runtime UX steps, not a planning-time prerequisite.
- [ ] **(Deferred / optional, flagged now)** A Microsoft-attestation-**signed**
      public release additionally needs an **EV code-signing certificate
      (~$250–560/yr)** + a **Microsoft Partner Center** account — a real paid /
      human prerequisite. This is **out of scope for Phase 6** (test-signed only);
      recorded here so it can be provisioned before any signed release is pursued
      (would gate a future release issue with `blocked:human`).

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| The driver is **C / KMDF (WDK)** opening PSM 0x1001 and exposing a **WDF device interface**; source in `driver/PodBridgeAAP` | Constitution: only a kernel driver may open a custom Classic-L2CAP PSM on Windows; architecture places it in `driver/PodBridgeAAP`; the WinPods KMDF bridge is the blueprint | 2026-07-09 |
| Driver↔user-mode I/O is a **WDF device interface + IOCTLs** (connect, send; an inverted-call / pending-IOCTL for inbound notifications) — DEFAULT | No documented facts prescribe the contract; IOCTL + inverted call is the standard KMDF pattern for asynchronous inbound frames and matches the WinPods reference shape | 2026-07-09 |
| `PodBridge.Core` holds `IAapTransport` + a clean-room `AapProtocol` module scoped to **noise-control only** for this phase; every opcode/constant is cited | Constitution clean-room + OS-free Core; keeps Phase 6 tightly bound (gestures = Phase 7) | 2026-07-09 |
| Use only the **cleartext 16-byte AAP handshake** and the plaintext control channel | Constitution Don'ts: never defeat MagicPairing crypto | 2026-07-09 |
| The advanced tier is **opt-in**; the driver installs via its **own INF + `pnputil`** in a separate, user-triggered **elevated** action that **also imports the self-signed test cert into Trusted Root CA / Trusted Publishers**; the app stays `asInvoker` | Constitution: no kernel driver/elevation without explicit opt-in; MSIX cannot cleanly bundle a driver; on x64 a test-signed driver will not load unless its cert is trusted, so cert-trust belongs in the same elevated opt-in step | 2026-07-09 |
| Ship **test-signed** using a self-signed test cert; document **both** load requirements (test-signing mode + test-cert trust) and their security trade-off honestly; do NOT pursue attestation signing in this phase — DEFAULT | Prior-art: even paid MagicPods ships test-mode; the April-2026 policy broke cross-signing; an EV cert/Partner Center is a paid prerequisite out of scope here. Honesty gate satisfied without over-committing | 2026-07-09 |
| The app **never runs `bcdedit`**; enabling test-signing is a documented manual user step, and the test-cert trust import happens only inside the explicit elevated opt-in install (never silently) | `bcdedit` is a `.claude/settings.json` deny rule and a machine-wide security change; cert trust is likewise machine-wide and is gated behind opt-in; honesty over automation | 2026-07-09 |
| `DriverAapTransport` **probes for the driver** and reports `Unavailable` when absent; it is the only path to the driver; with the driver absent Tier-1 works and its tests pass | Constitution graceful-degradation gate; architecture "reached only through DriverAapTransport" | 2026-07-09 |
| A set is **optimistic, then confirmed by the device echo**, reverting on a timeout — DEFAULT | Architecture flow 4 ("AirPods echo confirms → DeviceState updated"); timeout-revert keeps the UI truthful; encode/echo/timeout logic is device-independent and unit-tested | 2026-07-09 |
| Phase-6 reference model is **AirPods Pro 2 (USB-C)**; **Adaptive** is offered only where the connected model reports support; full model matrix + firmware fragility is Phase 8 — DEFAULT | Prior-art pins the open AAP spec to Pro 2 USB-C fw 7A305; roadmap puts model/firmware coverage in Phase 8 | 2026-07-09 |
| CI adds a **non-blocking, compile-only driver-build job (WDK/EWDK)**; functional behaviour is the **manual smoke test** on real hardware — DEFAULT | Catches driver compile regressions without a hardware gate; workflow says Tier-2 always needs a manual smoke test and CI cannot exercise Bluetooth | 2026-07-09 |
| The AAP noise-control byte format and the KMDF-L2CAP mechanics (incl. the exact test-cert trust-store step) are **research-intensive** → each splits into a `chore:research-*` issue (research comment) + an implementation issue depending on it | Workflow pre-classifies AAP byte-format and Windows-BT-API/signing confirmation as research-intensive (≥3 source lookups); research comments are named QA artefacts | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue
per step, grouped under a milestone. This spec owns the design; the issues own
progress.

- Milestone: created on merge (one per this phase; carries
  `Depends on milestone: #<Phase-5 milestone>` in its description).
- Issues: created from this spec once merged (one per implementable step).

Each issue references this spec path (`docs/specs/spec-advanced-driver-anc.md`).

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      `dotnet format --verify-no-changes`, and unit tests all green for the
      managed projects.
- [ ] The **`chore:research-aap-anc-protocol` research comment** is posted
      (sources + consensus on handshake bytes, notification-register, ANC opcode
      + Off/ANC/Transparency/Adaptive values, echo/read format) and the
      `AapProtocol` implementation reflects its consensus (research comment = QA
      artefact).
- [ ] The **`chore:research-kmdf-l2cap` research comment** is posted (sources +
      consensus on `BRB_L2CA_OPEN_CHANNEL` PSM 0x1001 open, the WDF device
      interface + IOCTL/inverted-call I/O model, INF + `pnputil` install, and the
      full load story — test-signing mode **and the exact test-cert trust-store
      step** (Trusted Root CA / Trusted Publishers) vs Microsoft attestation) and
      the driver + install flow reflect it (research comment = QA artefact).
- [ ] **Fake-`IAapTransport` unit tests** assert: (a) ANC/Transparency/Adaptive
      set packets and the read/notification parse encode/decode to the researched
      bytes; (b) a set is confirmed on echo and **reverted on timeout**; (c) with
      the transport reporting `Unavailable`, `DeviceState` exposes noise control
      as disabled and no packet is sent (device-independent; Tier-1 test gate).
- [ ] With the driver **uninstalled**, the app launches, all Tier-1 features work,
      the Tier-1 test suite passes, the noise-control menu is **disabled with an
      honest explanation**, and **no elevation** is requested.
- [ ] Every AAP opcode/constant in `AapProtocol` carries a **documented-fact
      citation comment**; **no GPL source or verbatim prose** is copied (review).
- [ ] No user-facing string claims a **Microsoft-signed / production** driver; the
      docs/UX state **both** x64 load requirements — **test-signing mode
      (`bcdedit`) AND trusting the self-signed test cert (import into Trusted Root
      CA / Trusted Publishers)** — and their security trade-off (review).
- [ ] The driver install is a **separate, user-triggered, elevated** step
      (`pnputil` **plus** the test-cert import into Trusted Root CA / Trusted
      Publishers); the app manifest is **`asInvoker`** and the app never invokes
      `bcdedit` (review + smoke).
- [ ] **(Manual smoke test — Tier-2 gate, real hardware)** With the driver
      installed (test-signing on **and the test cert trusted**) and real AirPods
      Pro 2 connected: switching Off / ANC / Transparency / Adaptive from the tray
      changes the device state, the **echo confirms** the applied mode, and an
      induced no-echo case **reverts** the UI with an error.
- [ ] **(Human QA gate)** The opt-in enable-advanced-tier flow — including
      **enabling test-signing mode and trusting the self-signed test certificate
      (Trusted Root CA / Trusted Publishers)** — is followable end-to-end by a
      user on a real machine, and the test-signed driver **actually loads** as a
      result.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| KMDF L2CAP open / PSM registration is under-documented and easy to get wrong | Confirmed up front by `chore:research-kmdf-l2cap` against Microsoft docs + the WinPods blueprint; the managed side is isolated behind `IAapTransport`, so the app is testable without the driver. |
| AAP byte format is firmware-fragile (pinned to Pro 2 USB-C) | `chore:research-aap-anc-protocol` records the exact fw the bytes were confirmed against; opcodes/constants are centralized + cited in one module; broad firmware coverage is deferred to Phase 8. |
| The test-signed driver silently fails to load because the honesty/opt-in flow only enables test-signing but never trusts the self-signed cert (x64 rejects the untrusted publisher) | Both load requirements are treated as first-class: `chore:research-kmdf-l2cap` fixes the exact trust-store step; the elevated opt-in install imports the cert into Trusted Root CA / Trusted Publishers; docs/UX state both; the human QA gate verifies the driver actually loads. |
| Driver signing is unsolved for a free OSS project (April-2026 policy) | Phase ships **test-signed** and is honest in UX/docs about the test-signing-mode + cert-trust trade-off; the attestation path (EV cert) is flagged as a deferred human prerequisite, not promised. |
| Enabling test-signing and trusting a self-signed cert weakens the user's machine security | The tier is strictly opt-in with an explicit warning naming both machine-wide changes; the app never enables test-signing automatically (no `bcdedit`) and imports the cert only inside the explicit elevated opt-in; Tier-1 remains fully functional without either. |
| CI cannot exercise Bluetooth / the driver | Managed Verify is the CI gate; a compile-only driver-build job catches driver regressions; functional behaviour is the manual smoke test on real hardware at the QA gate. |
| Feature creep from gestures/other AAP commands | Scope is bound to noise control; `IAapTransport` + `AapProtocol` are designed so Phase 7 (gestures) extends them without redesign. |

## Decision log

- 2026-07-09: Spec drafted (autonomous bulk planning). Genuinely-open points were
  settled with documented DEFAULTs rather than left open: the driver↔user-mode
  IOCTL/device-interface contract, the test-signed (not attestation) signing
  strategy, the optimistic-set/echo-confirm/timeout-revert model, the Pro 2
  reference model with model-gated Adaptive, and the non-blocking compile-only
  driver CI job. The AAP byte format and the KMDF-L2CAP mechanics are committed
  to the research-intensive two-issue split, with both research comments named as
  QA artefacts. The EV cert / Partner Center is flagged as a deferred human
  prerequisite for a future attestation-signed release (out of scope here).
- 2026-07-09: Spec-review fix — the test-signing honesty story was factually
  incomplete: on x64 a self-signed test-signed KMDF driver does not load under
  test-signing mode unless the test cert is also trusted (imported into Trusted
  Root CA / Trusted Publishers). Added the cert-trust step alongside "enable
  test-signing mode" in the honesty-UX/docs Outcome, the opt-in-install Outcome,
  the `bcdedit` constraint, and the corresponding Verification/QA items; made
  `chore:research-kmdf-l2cap` confirm the exact trust-store step; and put the
  cert import inside the explicit elevated opt-in install (never silent).