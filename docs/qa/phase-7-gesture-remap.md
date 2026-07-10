# PodBridge — Phase 7 Manual Test Guide (Advanced tier: gesture remap)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine. Phase 7 is part of the **OPTIONAL, opt-in advanced tier** (Tier 2) introduced in Phase 6 — it reuses that phase's kernel driver + AAP control channel and adds nothing that runs without it. Each case is tagged **[machine]** (no AirPods, no driver — repo/CI/unit checks + the driver-absent graceful-degradation path) or **[real-hardware+driver]** (needs the Phase-6 driver actually installed and loaded, plus AirPods Pro 2, on a real Windows 11 box) so the no-hardware cases can be batched first. The **[real-hardware+driver]** cases depend on the Phase-6 opt-in enablement (test-signing + a trusted test cert); PodBridge performs none of those steps silently.

## 1. Title & Scope

This guide verifies **Phase 7 — Gesture remap** (milestone #7), the second slice of the **opt-in advanced tier** (Tier 2). Implemented issues (all merged, **no parked issues** in this milestone): **#46** research (gesture AAP byte format + reconnect-overwrite), **#47** clean-room press-and-hold gesture builders in `AapProtocol` (Core) + byte-level unit tests, **#48** persist the gesture config + re-push it on every Tier-2 (re)connect, **#49** driver-gated gesture-remap settings UI with graceful degradation. Phase 7 adds:

- **Clean-room press-and-hold gesture builders** in the existing `PodBridge.Core` `AapProtocol` module: `BuildSetPressAndHoldGesture` emits the 11-byte SET frame `04 00 04 00 09 00 16 [right] [left] 00 00` (control-command identifier `0x16` = ClickHoldMode, per-bud: `data1` = right bud, `data2` = left bud), and `TryParsePressAndHoldGestureNotification` parses the identical inbound echo/notification frame. The action bytes are the only two documented settable values — **Noise Control = `0x01`, Siri = `0x05`** — modelled as the `GestureAction` enum, so an unsupported action can never be built or stored. Every constant carries a documented-fact citation to `docs/research/gesture-aap.md`.
- A **Core gesture-configuration model** (`GestureConfiguration`, a per-bud `RightBud`/`LeftBud` action map) behind an OS-free persistence abstraction (`IGestureConfigStore`), plus a **re-push policy** (`GestureRepushController`) that subscribes to the new Tier-2 `IAapTransport.Connected` (re)connect signal and re-writes the stored config on **every** (re)connect — because Apple firmware forgets a third-party host's control-command config across a disconnect. The write reuses the Phase-6 write+echo-confirm pattern with a **single** retry and no retry storm.
- The **Windows `GestureConfigStore`** adapter persisting the choice to a small per-user file `%LOCALAPPDATA%\PodBridge\gesture-config.txt` (format `right;left`, e.g. `NoiseControl;Siri`), local-only, best-effort (a read/write error never crashes the tray).
- A **driver-gated gesture-remap settings window** ("Gesture controls…", opened from the tray) backed by the Core `GestureSettingsController`. It resolves three honest states — **Available** (driver present + supported model → per-bud action pickers + Apply), **DriverUnavailable** (Tier-1 default → the reused Phase-6 driver-absent notice + the opt-in "Enable advanced tier…" affordance), and **ModelUnsupported** (driver present but the connected model is out of the Phase-7 scope) — so it degrades gracefully and is never silently broken. Applying persists the choice **before** the write, so even a non-fatal "couldn't apply" re-applies on the next reconnect.

Phase 7 keeps **Tier 1 fully intact and driver-free.** With the driver **absent** (the default), every Phase 1–6 Tier-1 feature still works, the .NET Verify suite passes, and the gesture window shows the driver-absent state with the opt-in affordance — **no crash, no elevation, no packet attempted.** The gesture feature is invasive only when the user has already opted into the Phase-6 advanced tier.

> **Honest scope (read this first).** Only the **press-and-hold** stem gesture is remappable. Single, double, and triple presses (play/pause, next, previous) are **fixed by Apple** and are deliberately not exposed — the code models only the two documented settable actions (Noise Control, Siri), so no unsupported action can be sent. The byte format is confirmed only on **AirPods Pro 2 (USB-C), firmware `7A305`**; the feature is gated to that reference model and hidden elsewhere. This narrower surface is the research-confirmed truth (#46), not a regression from the spec's earlier `0x14`/`0x15`/`0x16` shorthand — the spec's own decision is "the exposed action set is exactly what the research comment confirms is settable … no invented actions". See §6.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

Out of scope here: the KMDF L2CAP driver, `DriverAapTransport`'s driver-side logic, the `AapProtocol` write path / handshake, and the driver install / test-mode / signing UX — all **Phase 6**, consumed unchanged (Phase 7's only carve-out is the single `IAapTransport.Connected` (re)connect event added to the Core interface and raised from `DriverAapTransport`'s existing connect/handshake path). Noise-control switching is Phase 6. Broad model/firmware coverage, firmware-fragility hardening, and Tier-2 diagnostics are **Phase 8**. Battery / play-pause (Phase 2), codec + mic-mode display (Phase 3), the mic-profile policy (Phase 4), and packaging/About (Phase 5) are unchanged Tier-1 paths.

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** for any **[machine]** case — Tier 1 is driver-free and the app manifest is `asInvoker`. The **[real-hardware+driver]** cases require the Phase-6 advanced-tier enablement (admin for the explicit, opt-in install/test-signing steps — see the Phase-6 guide).
- Run all repo commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/CI checks (§4.2, §5.1): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the [real-hardware+driver] cases (§5.7–§5.9):** the **Phase-6 advanced tier already installed and loaded** on this box (its own build + test-sign + `bcdedit /set testsigning on` + trusted test cert + `pnputil` install — all per `docs/qa/phase-6-advanced-driver-anc.md`), real **AirPods Pro 2** (the reference model), and a working Bluetooth radio. *Not needed for the machine cases* — those are repo/CI/unit-test checks + the driver-absent path.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run (dev build, driver absent)

The dotnet **Verify** gate builds and tests the **managed** app the normal way; Phase 7 adds no driver code (the Phase-6 KMDF driver stays out of `PodBridge.slnx`). Run each from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt** — including when you click "Enable advanced tier…" in the gesture window, which only elevates the *launched installer* after an explicit warning, never PodBridge itself. (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) On a dev build the driver is **absent**, so the "Gesture controls…" window opens in the **driver-absent** state (pickers hidden, opt-in affordance shown) — this is the correct Tier-1 default, not a failure.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from the repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` across **two** test projects: **`PodBridge.Core.Tests` erfolgreich: 208, gesamt: 208** and **`PodBridge.Windows.Tests` erfolgreich: 29, gesamt: 29** (237 passed, 0 failed, 0 skipped). This includes the **device-independent Phase-7 gates**, all driven by fakes with **no driver and no hardware**:

- **`AapProtocolGestureTests`** (Core) — the clean-room byte format: `BuildSetPressAndHoldGesture` encodes `04 00 04 00 09 00 16 [right] [left] 00 00` for the four representative assignments (NoiseControl/Siri in each order and each shared), with the right-bud action at **byte 7** (`data1`) and the left-bud action at **byte 8** (`data2`); an asymmetric assignment is **not** symmetric on the wire; the config overload matches the two-arg overload; undefined action bytes throw; `TryParsePressAndHoldGestureNotification` round-trips a built SET frame and **rejects** the wrong length, wrong header, a non-`0x16` identifier (`0x0D` noise-control, `0x14`/`0x15` single/double-click), and undocumented action bytes (`0x00`/`0x02`/`0x06`); and one test writes the frame over a **fake `IAapTransport`** and asserts the exact bytes reach the wire.
- **`GestureConfigurationTests`** (Core) — the per-bud model: the shared-fallback factory assigns both buds, right/left are not interchangeable in record equality, and the `GestureAction` enum values equal the documented wire bytes (`NoiseControl` = `0x01`, `Siri` = `0x05`).
- **`GestureSupportTests`** (Core) — the model gate: press-and-hold is supported on **AirPods Pro 2 / Pro 2 USB-C only** and hidden on every other model; the exposed action set is exactly **Noise Control + Siri** on a supported model and **empty** otherwise (no invented actions).
- **`GestureRepushControllerTests`** (Core) — a fake `IAapTransport` + fake store + fake clock drive: the `Connected` (re)connect signal re-pushes the stored config; it re-pushes on **every** reconnect, **re-reading the store each time**; a missing echo **retries once then** reports `CouldNotApply` (no storm); a transport exception on send is a non-fatal `CouldNotApply` (never-throws invariant); nothing is sent when **no config is stored** (`NoConfiguration`) or the **transport is unavailable** (`Unavailable`); and `Dispose` unsubscribes so a late reconnect sends nothing.
- **`GestureSettingsControllerTests`** (Core) — the settings decision + apply: `GetAvailability` returns `DriverUnavailable` when the driver is absent (**even for a supported model**), `ModelUnsupported` for an unsupported model with the driver present, and `Available` for a supported model; `ApplyAsync` on an available surface **persists then writes** the frame and confirms on echo, on the driver-absent path **persists nothing / sends nothing** (`Unavailable`), and on a missing echo **still persists** the choice (`CouldNotApply`) so it re-applies on the next reconnect.
- **`GestureConfigStoreTests`** (Windows) — the file-backed store round-trips the per-bud choice across a fresh instance, returns `null` for a missing / malformed / wrong-shape value (so the re-push policy sends nothing), and overwrites a previous choice — all against a temp file, no `%LOCALAPPDATA%` writes.

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail. **Verify does not build the driver** — that is the separate Driver CI job (Phase 6, §5.1).

### 4.2 Static / repo inspections — [machine]

The dotnet build does not exercise the OS-free layering, the clean-room citations, or the research artefact — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **GESTURE-CORE-TYPES-EXIST** | `dir src\PodBridge.Core\Models\GestureAction.cs src\PodBridge.Core\Models\GestureConfiguration.cs src\PodBridge.Core\Protocol\GestureAvailability.cs src\PodBridge.Core\Protocol\GestureSupport.cs src\PodBridge.Core\Protocol\IGestureConfigStore.cs src\PodBridge.Core\Protocol\GestureSettingsController.cs src\PodBridge.Core\Protocol\GestureRepushController.cs src\PodBridge.Core\Protocol\GestureRepushOutcome.cs` | All exist; the gesture model, availability/support gates, persistence abstraction, and the decision/apply + re-push logic live in OS-free Core. |
| **GESTURE-WINDOWS-STORE-EXISTS** | `dir src\PodBridge.Windows\GestureConfigStore.cs` | Exists — the file-backed `IGestureConfigStore` adapter under `%LOCALAPPDATA%\PodBridge`. |
| **GESTURE-APP-UI-EXISTS** | `dir src\PodBridge.App\GestureSettingsWindow.xaml src\PodBridge.App\GestureSettingsWindow.xaml.cs` | Both exist — the driver-gated settings window (a thin binding over `GestureSettingsController`). |
| **CORE-IS-OS-FREE** | `findstr /I /C:"DllImport" /C:"P/Invoke" /C:"CreateFile" src\PodBridge.Core\Protocol\GestureRepushController.cs src\PodBridge.Core\Protocol\GestureSettingsController.cs src\PodBridge.Core\Protocol\AapProtocol.cs` | **No match** — Core carries no P/Invoke; the file store and driver transport are `PodBridge.Windows` adapters. |
| **CLEAN-ROOM-CITATIONS** | `findstr /N /C:"gesture-aap.md" src\PodBridge.Core\Protocol\AapProtocol.cs src\PodBridge.Core\Models\GestureAction.cs src\PodBridge.Core\Models\GestureConfiguration.cs` | Every gesture opcode/constant (identifier `0x16`, right/left byte order, action bytes `0x01`/`0x05`) carries a comment citing `docs/research/gesture-aap.md`; no GPL source / verbatim doc prose copied. |
| **ONLY-PRESS-AND-HOLD-SETTABLE** | `findstr /N /C:"0x14" /C:"0x15" /C:"0x16" src\PodBridge.Core\Protocol\AapProtocol.cs` and `type src\PodBridge.Core\Models\GestureAction.cs` | `0x16` (ClickHoldMode) is the only builder; `0x14`/`0x15` (Single/DoubleClickMode) appear **only** as recorded-not-shipped provenance comments (single/double/triple are Apple-fixed) — there is no builder for them. The `GestureAction` enum defines exactly `NoiseControl = 0x01` and `Siri = 0x05` (no invented actions). |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 7 — the gesture window's "Enable advanced tier…" reuses the Phase-6 flow that elevates a *separate* PowerShell step, never the app). |
| **CONNECT-EVENT-ON-INTERFACE** | `findstr /N /C:"Connected" src\PodBridge.Core\Protocol\IAapTransport.cs src\PodBridge.Windows\DriverAapTransport.cs` | The Tier-2 `(re)connect` signal is an `event EventHandler? Connected` on the **OS-free `IAapTransport`** interface, raised by `DriverAapTransport` only after a fresh L2CAP channel opens — so the Core re-push policy subscribes to the abstraction and a fake fires it in tests. |
| **RESEARCH-ARTEFACT** | `gh issue view 46 --repo bhemsen/PodBridge --comments`, then `dir docs\research\gesture-aap.md` | #46 has a `## Research:` comment (Sources / Consensus / Disputes) fixing the gesture bytes (identifier `0x16` under opcode `0x0009`; frame `04 00 04 00 09 00 16 [right] [left] 00 00`; per-bud right=`data1`/left=`data2`; actions `0x01` Noise Control / `0x05` Siri; single/double-click documented-but-not-settable; the reconnect-overwrite requirement; Pro 2 fw `7A305`) — reflected in `AapProtocol`. `docs/research/gesture-aap.md` present. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 5` | The latest `CI` (Verify) run on `main` is `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown.

**Reference — exact UI strings.**

- **Top-level tray context menu** (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · `Microphone mode` (submenu, Phase 4) · `Noise control` (submenu, Phase 6) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · **`Gesture controls…`** (Phase 7) · `About PodBridge` · `Exit`
- **Gesture window** — title bar **`Gesture controls`**; header title **`Press-and-hold gesture`**; header explanation (verbatim): **`AirPods let you reassign only the press-and-hold gesture. The single, double, and triple presses (play/pause, next, previous) are fixed by Apple and cannot be changed.`**; **`Close`** button.
- **Available state** (driver present + AirPods Pro 2):
  - Prompt: **`When you press and hold the stem, do:`**
  - Row labels **`Left AirPod`** and **`Right AirPod`**, each with a dropdown offering exactly: **`Cycle noise control`** and **`Siri / voice assistant`**
  - **`Apply`** button
  - Persistence note (verbatim): **`Your choice is saved and re-applied automatically each time your AirPods reconnect — AirPods forget a PC's setting otherwise.`**
  - Apply result line (one of): **`Applied to your AirPods.`** (confirmed) · **`Saved. Your AirPods didn't confirm it just now, so it'll be re-applied the next time they reconnect.`** (non-fatal miss) · **`The advanced-tier driver isn't available, so nothing was changed.`** (transport vanished)
- **Driver-absent state** (Tier-1 default) — the reused Phase-6 notice + opt-in affordance:
  - **`Requires the optional advanced tier (driver not installed)`** (reused verbatim from the Phase-6 `Noise control` notice — no new signed-driver claim)
  - **`Gesture remap needs the optional advanced-tier driver. Every default (Tier-1) feature keeps working without it.`**
  - **`Enable advanced tier…`** button (opens the same Phase-6 warning dialog / enable flow)
- **Model-unsupported state** (driver present, model out of scope): **`Connect supported AirPods (AirPods Pro 2) to change the press-and-hold gesture. Support for more models is planned.`**
- **Persisted config file:** `%LOCALAPPDATA%\PodBridge\gesture-config.txt`, contents `right;left` (e.g. `NoiseControl;Siri`).

---

### 5.1 Research comment (#46) present + reflected in code — [machine]
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 46 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Core\Protocol\AapProtocol.cs` (the gesture builders) and `docs\research\gesture-aap.md`.
- **Expected:** #46 carries a `## Research:` comment (Sources / Consensus / Disputes) fixing the gesture bytes — identifier `0x16` (ClickHoldMode) under control-command opcode `0x0009`; frame `04 00 04 00 09 00 16 [right] [left] 00 00`; per-bud addressing right=`data1`/left=`data2`; actions `0x01` Noise Control / `0x05` Siri; SingleClickMode `0x14` / DoubleClickMode `0x15` documented-but-not-settable (single/double/triple fixed by Apple); the reconnect-overwrite requirement; Pro 2 fw `7A305`. The consensus is reflected in `AapProtocol` and `docs/research/gesture-aap.md` is present.
- **Maps to:** issue #46; spec Verification (research comment = QA artefact).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 Press-and-hold builder emits the exact bytes — [machine]
- **Needs:** repo (the assertion is `AapProtocolGestureTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read `tests\PodBridge.Core.Tests\Protocol\AapProtocolGestureTests.cs` and `src\PodBridge.Core\Protocol\AapProtocol.cs` (`BuildSetPressAndHoldGesture`).
- **Expected:** For each representative assignment the SET frame is exactly `04 00 04 00 09 00 16 [right] [left] 00 00` — e.g. right=Noise Control / left=Siri → `04 00 04 00 09 00 16 01 05 00 00`; right=Siri / left=Noise Control → `… 16 05 01 00 00`; shared Noise Control → `… 16 01 01 00 00`. The right-bud action sits at **byte 7** (`data1`), the left at **byte 8** (`data2`) — asymmetric assignments are not symmetric on the wire. A built SET frame parses back to the same per-bud config, and non-`0x16` identifiers / unknown action bytes are rejected. One test proves the exact bytes reach the wire over a fake `IAapTransport`. No physical AirPods required.
- **Maps to:** issue #47; spec Outcome "gesture-remap packet builders … device-independent unit test asserts the exact byte sequence and per-bud addressing"; research #46.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Gesture config persistence round-trips (default = none) — [machine]
- **Needs:** repo (the assertion is `GestureConfigStoreTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read `tests\PodBridge.Windows.Tests\GestureConfigStoreTests.cs` and `src\PodBridge.Windows\GestureConfigStore.cs`.
- **Expected:** A saved per-bud `GestureConfiguration` round-trips across a fresh `GestureConfigStore` instance (stored as `right;left`, e.g. `NoiseControl;Siri`). **By default nothing is stored** — a missing file reads back as `null` ("no assignment yet"), and so do a malformed value (`NoiseControl;NotAnAction`) and a wrong-shape value (a single field) — so the re-push policy never sends an unsolicited config to an unconfigured device. A later save overwrites the previous choice. All best-effort: a read/write error never crashes the tray.
- **Maps to:** issue #48; spec Outcome "persisted to the app's existing local settings store"; `IGestureConfigStore`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Re-push-on-reconnect fires on a simulated (re)connect — [machine]
- **Needs:** repo (the assertion is `GestureRepushControllerTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read `tests\PodBridge.Core.Tests\Protocol\GestureRepushControllerTests.cs` and `src\PodBridge.Core\Protocol\GestureRepushController.cs`.
- **Expected:** Firing the fake `IAapTransport.Connected` (re)connect event re-pushes the stored config over the transport as the exact SET frame, confirmed by the echo. It re-pushes on **every** reconnect and **re-reads the store each time** (so a value changed between reconnects is what gets applied). A missing echo **retries once then** yields a non-fatal `CouldNotApply` (no storm; the next reconnect tries again); a transport send-exception is likewise a non-fatal miss (never-throws invariant). Nothing is sent when **no config is stored** (`NoConfiguration`) or the **transport is unavailable** (`Unavailable`). `Dispose` unsubscribes so a late reconnect sends nothing. This is the device-independent proof of the reconnect-overwrite mitigation — no driver involved (the event is on the Core abstraction).
- **Maps to:** issue #48; spec Outcome "re-push policy re-sends the stored config whenever the (re)connect event fires — device-independent unit test fires the event on a fake `IAapTransport`"; research #46 "reconnect-overwrite".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 Settings UI degrades gracefully — driver ABSENT: opt-in affordance, no packet, Tier-1 unaffected — [machine]
- **Needs:** the dev build (§3) or the installed MSIX, on a machine **without** the Phase-6 driver (the Tier-1 default). Backed by `GestureSettingsControllerTests` (§4.1).
- **Action:** Launch PodBridge with no driver installed. Right-click the tray → **`Gesture controls…`**. Read the window against the **Driver-absent state** reference above, then click **`Enable advanced tier…`** and immediately **Cancel** the warning dialog. Close the window. Exercise a Tier-1 feature (open `Microphone mode`, click `Refresh audio status`). Open Task Manager → Details and confirm `PodBridge.App.exe` is **not** elevated.
- **Expected:** The window opens in the **driver-absent** state — the per-bud pickers are **not** shown; instead the reused notice **`Requires the optional advanced tier (driver not installed)`**, the line **`Gesture remap needs the optional advanced-tier driver. Every default (Tier-1) feature keeps working without it.`**, and the **`Enable advanced tier…`** button appear. Clicking it shows the same Phase-6 warning dialog (`Enable the advanced tier`); **Cancel** changes nothing. **No gesture packet is ever attempted, no elevation happens, and there is no crash.** Every Tier-1 feature still works and the .NET Verify suite passes with the driver absent (§4.1). This is the constitution's graceful-degradation gate.
- **Maps to:** issue #49; spec Outcome "with the Phase-6 driver absent, the gesture UI is hidden/disabled, no gesture packet is ever attempted, the app does not crash, and the Tier-1 suite still passes"; `GestureSettingsControllerTests`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 Honest-signing + clean-room review — [machine]
- **Needs:** repo.
- **Action:** Read `src\PodBridge.Core\Protocol\AapProtocol.cs` (every gesture constant + its citation), and re-read all Phase-7 user-facing strings (the gesture window's three states, the persistence note, the Apply result lines).
- **Expected:** Each gesture opcode/constant carries a documented-fact citation to `docs/research/gesture-aap.md`; the code is a re-statement of reverse-engineered byte facts, **not** copied GPL source or verbatim protocol-doc prose; only the cleartext AAP control channel is used (MagicPairing is **not** defeated). The gesture window reuses the Phase-6 driver-absent notice **verbatim** and adds **no** new string claiming a Microsoft-signed / production driver; no string claims Apple-parity sound. The honest-scope header states plainly that only press-and-hold is remappable.
- **Maps to:** spec Verification "reuses the Phase-6 signing / test-mode notice and adds no signed-driver claim" + "every gesture opcode/constant carries a citation comment, no GPL source/prose copied"; constitution clean-room + honest-surface + no-MagicPairing rules.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

> **The remaining cases (§5.7–§5.9) require the Phase-6 driver to be built, trusted, and actually loaded on a real machine, plus AirPods Pro 2.** They are the **Tier-2 manual smoke test** — CI cannot sign-to-load or exercise Bluetooth. Set up the advanced tier first per `docs/qa/phase-6-advanced-driver-anc.md` (§5.7–§5.10). **Each Phase-6 enablement step is an explicit, honestly-warned user/admin action; PodBridge performs none of them silently.**

### 5.7 Assign press-and-hold per bud and confirm it takes effect — [real-hardware+driver]
- **Needs:** the Phase-6 driver loaded (test-signing on + cert trusted + `pnputil`-installed, rebooted), **AirPods Pro 2** connected. Verify the driver is present (`pnputil /enum-drivers` shows the `PodBridgeAAP.inf` package; the device appears in Device Manager under **Bluetooth**).
- **Action:** Relaunch PodBridge; confirm AirPods Pro 2 are connected. Right-click the tray → **`Gesture controls…`**. Set **Left AirPod = `Siri / voice assistant`** and **Right AirPod = `Cycle noise control`** (or any per-bud mix), then click **`Apply`**. On the AirPods, **press and hold** the left stem, then the right stem, and observe the behaviour.
- **Expected:** The window opens in the **Available** state with the two per-bud dropdowns (each offering exactly `Cycle noise control` and `Siri / voice assistant`) and the persistence note. On **Apply**, the result line shows **`Applied to your AirPods.`** (the AirPods echoed the config within ~2 s). Pressing and holding each stem now performs the assigned action — the **left** does Siri, the **right** cycles noise control — i.e. the per-bud, right-then-left addressing is correct on real hardware. No UAC prompt at runtime (interface I/O needs no elevation). If the AirPods don't confirm just now, the line reads **`Saved. Your AirPods didn't confirm it just now, so it'll be re-applied the next time they reconnect.`** — the choice is still persisted (non-fatal), which §5.9 exercises.
- **Maps to:** issue #47/#49; spec Verification Human-QA gate "assigning an action to a gesture takes effect on the AirPods"; research #46 per-bud byte order.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 Assignment persists to the local store — [real-hardware+driver]
- **Needs:** §5.7 done (an assignment applied).
- **Action:** Open `%LOCALAPPDATA%\PodBridge\gesture-config.txt` in a text editor (e.g. `type "%LOCALAPPDATA%\PodBridge\gesture-config.txt"`). Exit PodBridge, relaunch it, and re-open `Gesture controls…`.
- **Expected:** The file contains the applied choice as `right;left` (for the §5.7 mix, `NoiseControl;Siri`). After the relaunch, the window's pickers pre-select the persisted assignment (not the default first action). Local-only, no network.
- **Maps to:** issue #48; spec Outcome "persist that choice … the app's existing local settings store"; `GestureConfigStore`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Config is re-pushed automatically on reconnect (persists across disconnect) — [real-hardware+driver]
- **Needs:** §5.7 done (an assignment applied + confirmed), AirPods Pro 2.
- **Action:** With the assignment applied, **physically disconnect** the AirPods (put both buds in the case / power off, or toggle Bluetooth) so the Tier-2 L2CAP session drops, then **reconnect** them (take them out / power on). **Without** re-opening the gesture window or clicking Apply, press and hold each stem again and observe.
- **Expected:** After the reconnect the assigned press-and-hold actions **still work** — the app automatically re-pushed the stored config on the Tier-2 (re)connect (Apple firmware would otherwise have forgotten a third-party host's config on disconnect). No user action was needed between disconnect and the working gesture. This is the headline Phase-7 behaviour and the one thing CI cannot verify.
- **Maps to:** issue #48; spec Verification Human-QA gate "after physically disconnecting and reconnecting the buds, the assignment is automatically re-applied (re-push confirmed on real hardware)"; research #46 "reconnect-overwrite".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

## 6. Known limitations / honest scope (documented — do **not** reject Phase 7 for these)

- **Only press-and-hold is remappable.** Single, double, and triple presses (play/pause, next, previous) are **fixed by Apple** and are deliberately not exposed. The code models only ClickHoldMode (`0x16`); `0x14`/`0x15` (Single/DoubleClickMode) exist in the research only as recorded-not-shipped provenance and have no builder. This is the research-confirmed truth (#46), consistent with the spec's "no invented actions" decision — not a regression from the spec's earlier `0x14`/`0x15`/`0x16` shorthand.
- **Only Noise Control + Siri are settable actions.** These (`0x01` / `0x05`) are the only two documented action bytes; the `GestureAction` enum contains exactly them, so no other action can ever be built, stored, or offered.
- **Firmware-fragile; pinned to the reference model.** The byte format is verified against **AirPods Pro 2 (USB-C), firmware `7A305`** and centralised + cited in the single `AapProtocol` module. The feature is gated to Pro 2 (`GestureSupport`); every other model shows the honest "Connect supported AirPods (AirPods Pro 2)…" state. Broad model + firmware coverage and firmware-fragility hardening are **Phase 8**, not this phase.
- **Requires the Tier-2 advanced tier (a kernel driver).** Gesture remap only works when the opt-in Phase-6 driver is installed and loaded — which itself needs test-signing mode + a trusted test cert (two machine-wide, reversible security changes the user makes explicitly; PodBridge never runs `bcdedit`). With the driver absent (the default) the feature is cleanly unavailable and Tier 1 is untouched. Production (attestation / EV) driver signing remains **DEFERRED** (Phase 6 note).
- **No CI hardware — apply + re-push are a manual smoke test.** CI runs Verify (build/format/unit) only; it cannot load the driver or exercise Bluetooth. So §5.7–§5.9 (assign, persist, reconnect-re-push on real AirPods Pro 2) are only ever verified by a human at the QA gate (constitution Tier-2 gate; `docs/workflow.md`). A non-fatal "couldn't apply" (no echo) is expected and recoverable — the stored choice re-applies on the next reconnect.
- **MagicPairing is not defeated.** Only the **cleartext** AAP control channel is used (reusing the Phase-6 plaintext handshake + control frames); no crypto is broken (constitution Don'ts).

---

## 7. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #7** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, model + firmware, whether the Phase-6 driver was loaded (test-signing/cert-trust in place), and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** the gesture-remap milestone is **accepted** — the spec (`docs/specs/archive/spec-gesture-remap.md`) is archived, the roadmap Phase 7 link is repointed to the archive, and **milestone #7 is closed** (the orchestrator performs the merge + close), which unblocks **Phase 8 (Model & firmware coverage / hardening)**. The **[machine]** cases (§4, §5.1–§5.6) are the enforceable acceptance baseline; the **[real-hardware+driver]** cases (§5.7–§5.9) are the deferred human smoke test — record them when a suitable test box with the Phase-6 driver + AirPods Pro 2 are available. There are **no parked issues** in this milestone.

---

## 8. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- The gesture choice persists in `%LOCALAPPDATA%\PodBridge\gesture-config.txt`; delete it to reset to "no assignment yet" if you were only testing.
- Leaving the Phase-6 advanced tier installed/uninstalled and test-signing on/off is a Phase-6 cleanup concern — see `docs/qa/phase-6-advanced-driver-anc.md` §8.
