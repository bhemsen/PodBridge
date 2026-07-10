# PodBridge — Phase 8 Manual Test Guide (Model & firmware coverage / hardening)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine. Phase 8 is a **Tier-1 (driver-free) hardening + coverage** phase: it broadens PodBridge from the single Pro-2 reference model to the full AirPods line, adds a local (model, firmware-major) capability matrix, adds local-only diagnostics/logging, and hardens the prior-phase flows. It adds **no new kernel component** and **reopens no signing decision**. Each case is tagged **[machine]** (no AirPods, no driver — repo/CI/unit checks + the driver-absent path), **[real-airpods]** (needs real AirPods but **no driver** — Tier-1 model detection, diagnostics, radio-toggle), or **[real-airpods+driver]** (needs the **Phase-6 test-signed driver** installed and loaded plus AirPods — Tier-2 capability gating) so the no-hardware cases can be batched first. The **[real-airpods+driver]** cases depend on the Phase-6 opt-in enablement (test-signing + a trusted test cert); PodBridge performs none of those steps silently.

## 1. Title & Scope

This guide verifies **Phase 8 — Model & firmware coverage / hardening** (milestone #8), the closeable final milestone of the roadmap. Implemented issues (all merged, **no parked issues** in this milestone): **#50** research (Continuity model-identifier table), **#51** research (firmware-read existence + capability matrix), **#52** Core model registry + Unknown-AirPods fallback, **#53** `(model, firmware-major)` capability negotiation, **#54** local diagnostics export + structured logging, **#55** app-wide hardening pass + Continuity-parser fuzz tests. Phase 8 adds:

- A **Core model registry** (`IModelRegistry` / `ModelRegistry`, with the clean-room `AppleModelIdentifier` shape mapper) that resolves the Apple-Continuity model identifier to a strongly-typed `AirPodsModel` for all six vision models — **AirPods 2, 3, Pro, Pro 2 (Lightning + USB-C), Pro 3, Max (Lightning + USB-C)** — each with a per-model shape (`HasDualBuds` / `HasBatteryCase` / `HasInEarDetection`). Any unrecognised Apple audio device degrades to a labelled **"Unknown AirPods"** generic mode (best-effort dual-bud battery/in-ear, model-specific features off, `IsRecognized = false`) and **never crashes**. Every identifier carries a clean-room citation to `docs/research/model-ids.md` (issue #50, the sole content authority).
- A **Core capability provider** (`ICapabilityProvider` / `CapabilityProvider`) over a static `(model, firmware-major)` `CapabilityMatrix`. **Tier-1** features (`CaseBattery`, `InEarDetection`) gate on the **BLE-derived model axis only** — never firmware, never the driver — so they hold identically with the driver absent (the `IsTier1FeatureAvailable` method structurally takes no firmware/transport argument). **Tier-2** features (`NoiseControl`, `GestureRemap`, `ConversationAwareness`) gate on **both** driver presence **and** the matrix, returning an honest `CapabilityDecision` (reason string) in every state — never silently missing, never falsely claimed.
- A **local diagnostics snapshot** (`DiagnosticsSnapshot` + `DiagnosticsSnapshotFactory` / `DiagnosticsSnapshotBuilder` / `DiagnosticsSnapshotFormatter` in Core; `DiagnosticsExporter` in `PodBridge.Windows`; tray **"Export diagnostics"** in `PodBridge.App`) that writes a human-readable local file **and** copies it to the clipboard. It reports model, firmware-major, negotiated codec, tier, driver presence + honest signing/test-mode status, the full capability matrix, and recent BLE parse results — **address-masked, secret-free, no network call**.
- **Structured local logging** via `Microsoft.Extensions.Logging` with a hand-rolled `RollingFileLoggerProvider` (the **only** registered sink; `ClearProviders()` first). It writes to `%LOCALAPPDATA%\PodBridge\logs\podbridge.log`, rolls at **~10 MB**, purges rolled files older than **7 days**, defaults to **Information**, and raises to **Debug** at runtime via the tray **"Debug logging"** toggle — the local file only, never a network effect.
- An app-wide **hardening pass** (issue #55): a `BleScannerSupervisor` that restarts the Tier-1 watcher on a Bluetooth radio off→on toggle; graceful-shutdown audio-policy **restore** on exit; and a **Continuity-parser fuzz/property test** proving truncated/over-long/random payloads never throw and never mis-identify a known model.

Phase 8 keeps **Tier 1 fully intact and driver-free.** With the driver **absent** (the default) every Phase 1–7 Tier-1 feature still works, the .NET Verify suite passes, and every Tier-2 capability reports its honest driver-absent reason — **no crash, no elevation**.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

Out of scope here: the initial BLE scanner / Continuity parser / first model identification (**Phase 2**, broadened not created here); codec **detection** (**Phase 3**, only *reported* in the snapshot); the mic-profile policy engine (**Phase 4**, only its restore path is hardened); installer/MSIX/About (**Phase 5**); the KMDF L2CAP driver + AAP write path + signing UX (**Phase 6**, consumed unchanged — no new kernel component, no reopened signing decision); the gesture-remap feature itself (**Phase 7**, only *gated* in the matrix). Ongoing hardening beyond this milestone is handled as `track:adhoc` issues, not by reopening this spec.

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** for any **[machine]** or **[real-airpods]** case — Tier 1 is driver-free and the app manifest is `asInvoker`. The **[real-airpods+driver]** cases require the Phase-6 advanced-tier enablement (admin for the explicit, opt-in install/test-signing steps — see `docs/qa/phase-6-advanced-driver-anc.md`).
- Run all repo commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/CI checks (§4.2, §5.1): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the [real-airpods] cases (§5.7–§5.10):** at least two different real AirPods models (ideally several across AirPods 2/3/Pro/Pro 2/Pro 3/Max), a working Bluetooth radio. **No driver needed** — these exercise the driver-free Tier-1 registry, diagnostics, and radio-toggle paths.
- **For the [real-airpods+driver] cases (§5.11–§5.12):** the **Phase-6 advanced tier already installed and loaded** (its own build + test-sign + `bcdedit /set testsigning on` + trusted test cert + `pnputil` install — all per `docs/qa/phase-6-advanced-driver-anc.md`), plus real AirPods.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run (dev build, driver absent)

The dotnet **Verify** gate builds and tests the **managed** app the normal way; Phase 8 adds no driver code (the Phase-6 KMDF driver stays out of `PodBridge.slnx`). Run each from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt**. (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) On a dev build the driver is **absent**, so every Tier-2 capability row in a diagnostics export reads `off (requires the optional driver)` — this is the correct Tier-1 default, not a failure.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from the repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` across **two** test projects: **`PodBridge.Core.Tests` erfolgreich: 294, gesamt: 294** and **`PodBridge.Windows.Tests` erfolgreich: 36, gesamt: 36** (**330 passed, 0 failed, 0 skipped**). This includes the **device-independent Phase-8 gates**, all driven by fakes with **no driver and no hardware**:

- **`ModelRegistryTests`** (Core) — fixture Continuity payloads for **AirPods 2, 3, Pro, Pro 2 (Lightning + USB-C), Pro 3, Max (Lightning + USB-C)** each resolve to the correct `AirPodsModel` with the correct shape (dual-bud models → `HasBatteryCase`/`HasInEarDetection` true; AirPods Max → both false, `HasDualBuds` false); an **unrecognised identifier** yields the `"Unknown AirPods"` fallback (`IsRecognized = false`, best-effort dual-bud shape), never throwing.
- **`CapabilityMatrixTests`** (Core) — the static `(model, firmware-major)` matrix: noise-control is model-capable on Pro/Pro 2/Pro 3/Max and **not** on AirPods 2/3; gesture-remap delegates to the Phase-7 `GestureSupport` gate (Pro 2 only) so it never over-claims; conversation-awareness on Pro 2/Pro 3 only. With the shipped **empty** `unsupportedFirmware` set, a null (unreadable) or any firmware-major on a capable model evaluates to `Supported` — the firmware axis is a **documented no-op** today.
- **`CapabilityProviderTests`** (Core) — with a fake `IModelRegistry` + fake `IAapTransport`: **Tier-1** gating (`IsTier1FeatureAvailable`) consults the model shape only and holds identically with the transport unavailable (AirPods Max hides `CaseBattery`); **Tier-2** (`GetTier2Capability`) is off with **`requires the optional driver`** when the transport is unavailable; with the driver present but firmware unreadable a Phase-6/7-supported model stays **on** (`supported`, no regression); a model without the feature is off with **`not supported on this model`**; a firmware-major in the (seam) unsupported set is off with **`not supported on this firmware`**.
- **`UnknownModelDegradeTests`** (Core) — the Unknown fallback keeps best-effort Tier-1 battery/in-ear and leaves every Tier-2 feature off, with no crash.
- **`ContinuityParserFuzzTests`** (Core) — truncated, over-long, and random Continuity payloads **never throw** and **never mis-identify** a known model's fixture (the hardening gate; runs inside Verify).
- **`BleScannerSupervisorTests`** (Core) — with a fake `IBleScanner` + fake `IBluetoothRadioSource`: `Start` starts the scanner unconditionally; a radio **off** edge stops it; a radio **off→on** edge does a fresh `Stop()` then `Start()` (the WinRT watcher does not resume by itself); redundant same-state events are ignored; `Dispose` unsubscribes.
- **`MicPolicyEngineRestoreTests`** (Core) — with a fake `IAudioPolicy`: the graceful-shutdown `Restore()` path returns the default endpoints to the prior routing, and a policy failure during restore is swallowed (never throws).
- **`BleParseHistoryRecorderTests`** / **`DiagnosticsSnapshotBuilderTests`** / **`DiagnosticsSnapshotFactoryTests`** / **`DiagnosticsSnapshotFormatterTests`** (Core) — the snapshot is **deterministic** (same inputs → equal record, no timestamp/random in content); the recent-parse history keeps only the **masked** address (`**:**:**:**:AB:CD` form); the formatter renders the model/firmware/codec/tier/driver/capability sections as plain text; firmware-major renders as **`unknown (no host-requestable read exists today)`** when null.
- **`DiagnosticsExporterTests`** (Windows) — via a fake filesystem + fixed clock: the export writes a timestamped file under the local diagnostics folder and returns the same text for the clipboard; a **reflection assertion** confirms the type references **no network-capable type** (local-only).
- **`RollingFileLoggerProviderTests`** (Windows) — the sink writes lines, **rolls** the active file past the size cap into a timestamped `podbridge-*.log`, **purges** rolled files older than the age cap, and the `MinLevel` toggle raises/lowers verbosity — all against a temp directory, no network.

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail. **Verify does not build the driver** — that is the separate Driver CI job (Phase 6).

### 4.2 Static / repo inspections — [machine]

The dotnet build does not exercise the OS-free layering, the clean-room citations, the local-only sink, or the research artefacts — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **MODEL-REGISTRY-TYPES-EXIST** | `dir src\PodBridge.Core\Protocol\IModelRegistry.cs src\PodBridge.Core\Protocol\ModelRegistry.cs src\PodBridge.Core\Protocol\AppleModelIdentifier.cs src\PodBridge.Core\Models\AirPodsModelInfo.cs` | All exist; the registry maps all six models + the `"Unknown AirPods"` fallback, in OS-free Core. |
| **CAPABILITY-TYPES-EXIST** | `dir src\PodBridge.Core\Capabilities\ICapabilityProvider.cs src\PodBridge.Core\Capabilities\CapabilityProvider.cs src\PodBridge.Core\Capabilities\CapabilityMatrix.cs src\PodBridge.Core\Capabilities\CapabilityDecision.cs src\PodBridge.Core\Capabilities\FirmwareRefinement.cs` | All exist; the `(model, firmware-major)` matrix + honest-reason decision live in Core. |
| **DIAGNOSTICS-TYPES-EXIST** | `dir src\PodBridge.Core\Diagnostics\DiagnosticsSnapshot.cs src\PodBridge.Core\Diagnostics\DiagnosticsSnapshotFormatter.cs src\PodBridge.Core\Diagnostics\IDiagnosticsExporter.cs src\PodBridge.Windows\DiagnosticsExporter.cs src\PodBridge.Windows\Logging\RollingFileLoggerProvider.cs` | All exist; snapshot model + formatter in Core, file writer + log sink in `PodBridge.Windows`. |
| **CORE-IS-OS-FREE** | `findstr /I /C:"DllImport" /C:"P/Invoke" /C:"CreateFile" /C:"System.Net" src\PodBridge.Core\Capabilities\CapabilityProvider.cs src\PodBridge.Core\Diagnostics\DiagnosticsSnapshotFactory.cs src\PodBridge.Core\Protocol\ModelRegistry.cs` | **No match** — Core carries no P/Invoke and no network type; the file writer + log sink are `PodBridge.Windows` adapters. |
| **CLEAN-ROOM-CITATIONS (models)** | `findstr /N /C:"model-ids.md" src\PodBridge.Core\Protocol\AppleModelIdentifier.cs` | Every model identifier (AirPods 2 `0x0F20`, 3 `0x1320`, Pro `0x0E20`, Pro 2 `0x1420`/`0x2420`, Pro 3 `0x2720`, Max `0x0A20`/`0x1F20`) carries a comment citing `docs/research/model-ids.md`; no GPL source / verbatim prose copied. |
| **CLEAN-ROOM-CITATIONS (capability)** | `findstr /N /C:"firmware-capabilities.md" src\PodBridge.Core\Capabilities\CapabilityMatrix.cs` | The capability facts cite `docs/research/firmware-capabilities.md`; the firmware-major dimension is documented as a no-op (empty `unsupportedFirmware`). |
| **NO-FIRMWARE-READ-OPCODE-ADDED** | `findstr /I /C:"0x001D" /C:"0x1D" /C:"firmware" src\PodBridge.Core\Protocol\AapProtocol.cs` | Research #51 confirmed there is **no host-requestable** firmware read on the cleartext AAP channel (`0x001D` is an accessory-initiated, unsolicited push). **No new read opcode** was added to `AapProtocol`; the matrix's firmware axis is a documented no-op. |
| **LOCAL-ONLY-LOG-SINK** | `findstr /I /C:"System.Net" /C:"Socket" /C:"Http" src\PodBridge.Windows\Logging\RollingFileLoggerProvider.cs src\PodBridge.Windows\Logging\RollingFileLogger.cs` | **No match** — the only sink is the local rolling file; no network sink (constitution: local-only). |
| **ADDRESS-MASKED** | `findstr /I /C:"MaskedAddress" src\PodBridge.Core\Diagnostics\BleParseResult.cs` and `type src\PodBridge.Core\Diagnostics\DiagnosticsSnapshotFormatter.cs` | The snapshot keeps only the masked address (`**:**:**:**:AB:CD`); the full 48-bit address is never retained/rendered. |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 8 — no new kernel component, no elevation). |
| **DIAGNOSTICS-ARCHITECTURE** | `findstr /I /C:"IModelRegistry" /C:"ICapabilityProvider" docs\architecture.md` | `docs/architecture.md` lists the new Core types (`IModelRegistry`, `ICapabilityProvider`, diagnostics/logging boundary) per the spec Outcome. |
| **RESEARCH-ARTEFACTS** | `gh issue view 50 --repo bhemsen/PodBridge --comments`, `gh issue view 51 --repo bhemsen/PodBridge --comments`, then `dir docs\research\model-ids.md docs\research\firmware-capabilities.md` | #50 `## Research: AirPods Continuity model identifiers` (Sources / Consensus / Disputes) with the identifier→model table; #51 `## Research: AirPods firmware capability matrix` stating **no host-requestable firmware read exists** (`0x001D` is push-only) and the model-keyed capability matrix. Both docs present and reflected in code. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 5` | The latest `CI` (Verify) run on `main` is `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown.

**Reference — exact UI strings.**

- **Top-level tray context menu** (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · `Microphone mode` (submenu, Phase 4) · `Noise control` (submenu, Phase 6) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · `Gesture controls…` (Phase 7) · **`Export diagnostics`** (Phase 8) · **`Debug logging`** (Phase 8, checkable) · `About PodBridge` · `Exit`
- **Export diagnostics — success notification:** title **`Diagnostics exported`**, body **`Saved to {path} and copied to the clipboard.`** (`{path}` is the written file, e.g. `…\PodBridge\diagnostics\podbridge-diagnostics-20260710-143022.txt`).
- **Export diagnostics — failure notification** (disk error): title **`Diagnostics export failed`**, body **`Could not write the diagnostics file.`**
- **Diagnostics file location:** `%LOCALAPPDATA%\PodBridge\diagnostics\podbridge-diagnostics-{yyyyMMdd-HHmmss}.txt`.
- **Diagnostics file contents** (plain text; `DiagnosticsSnapshotFormatter`):
  ```
  PodBridge diagnostics
  Generated: {yyyy-MM-dd HH:mm:ss zzz}

  Model: {display name} ({AirPodsModel})
  Firmware major: {N or "unknown (no host-requestable read exists today)"}
  Codec: {AAC / SBC / Unknown}
  Tier: {"Tier 1 (driver-free)" or "Tier 2 (advanced tier, driver present)"}
  Driver present: {True / False}
  Driver signing/test-mode status: {see below}

  Capability matrix:
    Tier1.CaseBattery: {on / off} ({reason})
    Tier1.InEarDetection: {on / off} ({reason})
    Tier2.NoiseControl: {on / off} ({reason})
    Tier2.GestureRemap: {on / off} ({reason})
    Tier2.ConversationAwareness: {on / off} ({reason})

  Recent BLE parse results (address-masked):
    (none observed yet)
    **:**:**:**:AB:CD: {parsed / unparsed} (model: {AirPodsModel or n/a})
  ```
- **Capability reason strings** (verbatim): on → **`supported`**; driver absent → **`requires the optional driver`**; model lacks the feature → **`not supported on this model`**; firmware explicitly unsupported → **`not supported on this firmware`** (reserved; the shipped matrix produces no such row today).
- **Driver signing/test-mode status strings** (verbatim):
  - Driver absent: **`No optional driver installed (Tier-1 default — no admin, no elevation).`**
  - Driver present: **`Optional driver present: a self-signed, test-mode-only driver (never Microsoft-signed) — requires Windows test-signing mode, which the user enabled themselves.`**
- **Model display names** (verbatim): `AirPods 2` · `AirPods 3` · `AirPods Pro` · `AirPods Pro 2` (both connector variants) · `AirPods Pro 3` · `AirPods Max` (both connector variants) · `Unknown AirPods` (fallback).
- **Log file:** `%LOCALAPPDATA%\PodBridge\logs\podbridge.log` (active), rolled files `podbridge-{yyyyMMddHHmmssfff}.log`; rolls at ~10 MB, rolled files purged after 7 days.

---

### 5.1 Research comments (#50, #51) present + reflected in code — [machine]
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 50 --repo bhemsen/PodBridge --comments` and `gh issue view 51 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Core\Protocol\AppleModelIdentifier.cs`, `src\PodBridge.Core\Capabilities\CapabilityMatrix.cs`, and `docs\research\model-ids.md` / `docs\research\firmware-capabilities.md`.
- **Expected:** #50 carries `## Research: AirPods Continuity model identifiers` (Sources / Consensus / Disputes) fixing the 2-byte model identifier at the Proximity-Pairing field and the identifier→`AirPodsModel` table (AirPods 2 `0x0F20`, 3 `0x1320`, Pro `0x0E20`, Pro 2 `0x1420`/USB-C `0x2420`, Pro 3 `0x2720`, Max `0x0A20`/USB-C `0x1F20`), with the connector-variants-fold and Unknown-fallback decisions — reflected in `AppleModelIdentifier`. #51 carries `## Research: AirPods firmware capability matrix` stating explicitly that **no host-requestable firmware-version read exists** on the cleartext channel (`0x001D` "Device Information" is accessory-initiated and unsolicited), that Tier-1 gates on the model axis only, that firmware-unreadable falls back to the Phase-6/7 model-level capability, and the model-keyed capability matrix — reflected in `CapabilityMatrix` (no read opcode added; firmware axis is a no-op).
- **Maps to:** issues #50/#51; spec Verification (research comments = QA artefacts).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 Model registry maps all six models + Unknown fallback — [machine]
- **Needs:** repo (the assertion is `ModelRegistryTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read `tests\PodBridge.Core.Tests\Protocol\ModelRegistryTests.cs` and `src\PodBridge.Core\Protocol\AppleModelIdentifier.cs`.
- **Expected:** Each of the six models resolves to the correct `AirPodsModel` and shape — the five dual-bud models (`AirPods 2/3/Pro/Pro 2/Pro 3`) get `HasDualBuds`/`HasBatteryCase`/`HasInEarDetection` = true; `AirPods Max` gets all three false (single over-ear unit, no battery-reporting case, head on/off detection). An unrecognised identifier yields `"Unknown AirPods"` with `IsRecognized = false` and a best-effort dual-bud shape — never a throw, never a blank. No physical AirPods required.
- **Maps to:** issue #52; spec Verification "model-registry unit test".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Tier-1 model-axis gating never consults firmware or the driver — [machine]
- **Needs:** repo (the assertion is `CapabilityProviderTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read `tests\PodBridge.Core.Tests\Capabilities\CapabilityProviderTests.cs`.
- **Expected:** `IsTier1FeatureAvailable(CaseBattery, …)` is **true** for dual-bud models and **false** for AirPods Max (no case) — decided on the model shape alone. The method takes **no** firmware or transport argument, so the answer is identical whether the fake transport reports available or unavailable (Tier-1 independence, constitution graceful degradation). No physical device.
- **Maps to:** issue #53; spec Verification "Tier-1 model-axis gating unit test".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Tier-2 capability gating is honest in every state — [machine]
- **Needs:** repo (the assertion is `CapabilityProviderTests` + `CapabilityMatrixTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read the two test files and `src\PodBridge.Core\Capabilities\CapabilityProvider.cs`.
- **Expected:** For each Tier-2 feature: with the driver **absent**, `GetTier2Capability` is off with reason **`requires the optional driver`**. With the driver **present** but firmware-major **unreadable** (null — today's every reading), a Phase-6/7-supported model stays **on** (`supported`) — no regression of shipped ANC/gestures. A model without the feature is off with **`not supported on this model`**. A firmware-major placed in the (seam) unsupported set is off with **`not supported on this firmware`** — a code path proven by test though the shipped set is empty. The on-state holds only when **both** driver presence and capability hold. No physical device.
- **Maps to:** issue #53; spec Verification "capability-gating unit test".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 Continuity-parser fuzz + hardening tests pass — [machine]
- **Needs:** repo (the assertions are `ContinuityParserFuzzTests`, `BleScannerSupervisorTests`, `MicPolicyEngineRestoreTests`, `UnknownModelDegradeTests`, run in §4.1).
- **Action:** Confirm §4.1 passed; optionally re-read the four test files.
- **Expected:** The fuzz test feeds truncated / over-long / random Continuity payloads and asserts the parser + registry **never throw** and **never mis-identify** a known model's fixture. The supervisor test proves a radio off→on toggle does a fresh `Stop()`+`Start()`. The restore test proves the audio-policy `Restore()` returns endpoints and swallows a forced failure. The unknown-model test proves graceful degradation (best-effort Tier-1, Tier-2 off, no crash). All device-independent, inside Verify.
- **Maps to:** issue #55; spec Verification "parser fuzz/property test", hardening pass.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 Diagnostics export writes a masked, secret-free, local file + clipboard — [machine]
- **Needs:** the dev build (§3) or installed MSIX (driver absent is fine).
- **Action:** Launch PodBridge. Right-click the tray → **`Export diagnostics`**. Read the **`Diagnostics exported`** toast for the file path, then open the file (`type "<path>"`) and paste the clipboard (Ctrl+V) into a text editor.
- **Expected:** A toast titled **`Diagnostics exported`**, body **`Saved to {path} and copied to the clipboard.`**. The file opens as readable text with the sections shown in the reference block: `Model:` (display name + enum), `Firmware major: unknown (no host-requestable read exists today)`, `Codec:`, `Tier: Tier 1 (driver-free)`, `Driver present: False`, `Driver signing/test-mode status: No optional driver installed (Tier-1 default — no admin, no elevation).`, the five-row `Capability matrix:` (all Tier-2 rows **`off (requires the optional driver)`** with the driver absent), and `Recent BLE parse results (address-masked):` where any address is masked to `**:**:**:**:XX:XX` — **no full 48-bit address, no secret/token/key anywhere**. The clipboard text matches the file byte-for-byte. No network access occurs.
- **Maps to:** issue #54; spec Verification "diagnostics-snapshot unit test" + "tray Export diagnostics writes a local file + clipboard".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.7 Debug-logging toggle raises verbosity to the local file only; log rolls + caps — [machine]
- **Needs:** the dev build (§3) or installed MSIX.
- **Action:** Open `%LOCALAPPDATA%\PodBridge\logs\` (`explorer "%LOCALAPPDATA%\PodBridge\logs"`) and note `podbridge.log`. In the tray menu click **`Debug logging`** so it becomes checked, exercise the app (open submenus, `Refresh audio status`), then open `podbridge.log`. Confirm the checkbox state persists while the app runs. Confirm no `System.Net`/socket activity (the sink is a file). If you can generate volume, verify the active file rolls at ~10 MB into a timestamped `podbridge-*.log` and that rolled files older than 7 days are purged.
- **Expected:** With `Debug logging` **off** the file holds Information-and-above lines; toggling it **on** adds Debug lines at runtime with **no restart**. All output goes to the local file only — there is **no network sink** (the only registered provider is `RollingFileLoggerProvider`). The active file rolls at ~10 MB and rolled files are age-capped at 7 days — the log never grows unbounded.
- **Maps to:** issue #54; spec Verification "Debug logging toggle raises verbosity to the local file only; the log file rolls and is capped".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

> **The following [real-airpods] cases (§5.8–§5.10) need real AirPods but NO driver — they exercise the driver-free Tier-1 registry, diagnostics, and radio-toggle paths.**

### 5.8 Per-model detection across the AirPods line — [real-airpods]
- **Needs:** at least two, ideally several, real AirPods models (AirPods 2/3/Pro/Pro 2/Pro 3/Max), no driver.
- **Action:** For each available model in turn: connect it to Windows, launch/relaunch PodBridge, let it settle, then **`Export diagnostics`** and read the `Model:` line. Note battery behaviour: dual-bud models should report case battery; AirPods Max should not.
- **Expected:** Each connected model is identified with the correct display name (`AirPods 2` / `AirPods 3` / `AirPods Pro` / `AirPods Pro 2` / `AirPods Pro 3` / `AirPods Max`; both Pro 2 and both Max connector variants collapse to `AirPods Pro 2` / `AirPods Max`). For dual-bud models `Tier1.CaseBattery` reads `on (supported)`; for **AirPods Max** it reads `off (not supported on this model)` and `Tier1.InEarDetection` likewise off — the model-axis shape difference is correct on real hardware. **Note (see §6):** AirPods Pro 3 (`0x2720`) is pattern-extrapolated — confirm its detection explicitly here.
- **Maps to:** spec Verification Human-QA gate "at least two different AirPods models are correctly identified"; issue #52.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Unknown-model fallback on unrecognised hardware — [real-airpods]
- **Needs:** any Apple audio device whose identifier is outside the six models (e.g. 1st-gen AirPods, AirPods 4, a Beats model), or simply confirm via the fixture test (§5.2) if none is available.
- **Action:** Connect the unrecognised device, launch PodBridge, `Export diagnostics`, read the `Model:` line.
- **Expected:** The `Model:` line reads **`Unknown AirPods (Unknown)`**; best-effort dual-bud battery/in-ear is still attempted and every Tier-2 row is off. **No crash.** If no such device is available, this is covered device-independently by `ModelRegistryTests` / `UnknownModelDegradeTests` (§4.1) — record it as covered-by-test.
- **Maps to:** spec Outcome "Unknown AirPods generic mode … never crashes"; issue #52.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.10 Bluetooth radio off→on restarts the BLE watcher cleanly — [real-airpods]
- **Needs:** real AirPods, a working Bluetooth radio, no driver.
- **Action:** With PodBridge running and AirPods connected (battery visible), turn the PC's **Bluetooth radio OFF** (Action Center toggle or `Open Bluetooth settings`), wait a few seconds, then turn it **back ON** and reconnect the AirPods. Observe the tray battery/status line recovering **without** restarting PodBridge.
- **Expected:** On the radio **off** edge the watcher stops (status goes to disconnected/unknown); on the **off→on** edge the `BleScannerSupervisor` restarts a **fresh** watcher and battery/status resume once the AirPods reconnect — no PodBridge restart needed, no crash. (The WinRT watcher does not resume by itself after a radio power cycle; the supervisor's `Stop()`+`Start()` is what recovers it.)
- **Maps to:** issue #55; spec Verification Human-QA gate "radio toggle triggers a clean BLE-watcher restart".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

> **The following [real-airpods+driver] cases (§5.11–§5.12) require the Phase-6 driver built, trusted, and loaded (per `docs/qa/phase-6-advanced-driver-anc.md`) plus real AirPods.** They are the Tier-2 capability-gating smoke test — CI cannot sign-to-load or exercise Bluetooth.

### 5.11 Tier-2 gating with the driver present matches the capability matrix — [real-airpods+driver]
- **Needs:** the Phase-6 driver loaded (test-signing on + cert trusted + `pnputil`-installed, rebooted), real AirPods (ideally a Pro 2 and a non-Pro-2 model to contrast).
- **Action:** With the driver loaded and AirPods connected, `Export diagnostics` and read the capability matrix rows. Contrast a Pro 2 (ANC + gesture-remap capable) against a model like AirPods 2/3 (no ANC hardware).
- **Expected:** With the driver present, `Tier:` reads **`Tier 2 (advanced tier, driver present)`** and `Driver signing/test-mode status:` reads the **self-signed test-mode** string. Capability rows match the matrix for the connected model: on **AirPods Pro 2** `Tier2.NoiseControl` and `Tier2.GestureRemap` read **`on (supported)`**; on **AirPods 2/3** `Tier2.NoiseControl` reads **`off (not supported on this model)`** and `Tier2.GestureRemap` off (Phase-7 gate is Pro 2 only). Because no host-requestable firmware read exists, firmware-major is `unknown` and a supported model is **not** regressed by the unreadable firmware (it stays on).
- **Maps to:** issue #53; spec Verification Human-QA gate "ANC/gesture availability matches the capability matrix … firmware unreadable does not regress previously-working ANC/gestures".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.12 Diagnostics export produced on the driver machine is complete + masked — [real-airpods+driver]
- **Needs:** §5.11 (driver loaded, AirPods connected).
- **Action:** `Export diagnostics` on the driver machine; open the file and confirm every field is populated for a Tier-2 session.
- **Expected:** The file shows the real model, `Firmware major: unknown (no host-requestable read exists today)`, the negotiated codec, `Tier 2 (advanced tier, driver present)`, `Driver present: True`, the honest self-signed test-mode signing string, the full five-row capability matrix reflecting the connected model, and address-masked recent parses — **no full address, no secret**. This is the complete, address-masked bug-report artefact from a real Tier-2 machine.
- **Maps to:** issue #54; spec Verification Human-QA gate "a diagnostics export produced on the machine is complete and address-masked".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

## 6. Known limitations / known debt (documented — do **not** reject Phase 8 for these)

These were surfaced in the phase reviews/research and are recorded honestly. None blocks acceptance; the follow-ups are `track:adhoc` candidates.

1. **`ICapabilityProvider` is not yet the single live authority.** The provider is currently consumed **only** by the diagnostics snapshot (`DiagnosticsSnapshotFactory`). The live tray/gesture surfaces still gate through their **Phase-6/7 paths** (`NoiseControlSupport` / `GestureSupport` / `IAapTransport.IsAvailable` directly), not through `ICapabilityProvider`. So the diagnostics matrix is authoritative, but the noise-control submenu and gesture window are not yet driven by it. **Follow-up:** make `ICapabilityProvider` the single live gating authority for the tray and gesture controllers.
2. **`CapabilityMatrix.HasNoiseControl` is broader than the hardware-verified reference.** Noise-control model capability is asserted for Pro / Pro 2 / Pro 3 / Max, but only **AirPods Pro 2** is hardware-verified (Phase 6). The AAP noise-control byte format is confirmed only on Pro 2 USB-C fw `7A305`; the other models' noise-control support is derived from documented facts, not device captures. **Verify on real hardware before trusting noise-control switching on models other than Pro 2.**
3. **Pro 3 is extrapolated; Max 2 is out of scope.** The **AirPods Pro 3** identifier `0x2720` is pattern-extrapolated (two independent trackers, but neither shows an explicit packet capture) and is flagged for a real-hardware re-check (§5.8). **AirPods Max 2** (`0x2D20`, H2 chip, announced March 2026) is a newer model outside the vision's original six and deliberately resolves to the **`Unknown AirPods`** fallback rather than being silently folded into `AirPods Max`.
4. **The firmware-major capability dimension is a documented NO-OP.** Research #51 confirmed there is **no host-requestable firmware-version read** on the cleartext AAP channel — opcode `0x001D` ("Device Information") is an accessory-initiated, unsolicited one-time push, not a request/response. No source documents a firmware-major that toggles a whole Tier-2 feature on otherwise-identical hardware. Therefore firmware-major **never gates a feature today**: the `unsupportedFirmware` set ships empty and every reading is `null` (unknown). The `FirmwareRefinement` type + the `not supported on this firmware` reason are a **tested seam** so a future QA-confirmed refinement is a data edit, not a code change.
5. **Minor code debt (all non-blocking, teardown/edge-case only):**
   - `src/PodBridge.Core/Diagnostics/IDiagnosticsExporter.cs` holds **two public types** (`DiagnosticsExportResult` and `IDiagnosticsExporter`) — a one-type-per-file convention deviation.
   - `RollingFileLoggerProvider`'s roll filename uses **millisecond** precision (`yyyyMMddHHmmssfff`); two rolls inside the same millisecond would collide (extremely unlikely at a ~10 MB roll cadence).
   - `App.OnExit` calls `_micPolicyEngine?.Restore()` **without a try/catch**, despite the restore path's "best-effort / never throws" intent — a hardened teardown would wrap it defensively.
   - `WinRtBluetoothRadioSource` subscribes to `Radio.StateChanged` **outside the lock** (after releasing `_gate` in `AttachAsync`), a narrow teardown race with `Stop()`.
   - `BleScannerSupervisor` assumes **radio-on at startup** (`_radioOff = false` in `Start()`); a machine booted with the Bluetooth radio already off is not modelled until the first observed transition (Tier-1 still starts scanning unconditionally, so this only affects the first toggle-restart edge).

Additional standing constraints carried forward from prior phases (unchanged): **MagicPairing is not defeated** (cleartext AAP only); **no CI hardware** — all real-AirPods and Tier-2 behaviour is a manual smoke test; production (attestation/EV) driver signing remains **DEFERRED** (Phase 6).

---

## 7. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #8** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, connected model, whether the driver was loaded, and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** the model-coverage/hardening milestone is **accepted** — the spec (`docs/specs/archive/spec-model-coverage-hardening.md`) is archived, the roadmap Phase 8 link is repointed to the archive, and **milestone #8 is closed** (the orchestrator performs the merge + close). This is the **final** roadmap milestone; there is no successor phase to unblock. The **[machine]** cases (§4, §5.1–§5.7) are the enforceable acceptance baseline; the **[real-airpods]** (§5.8–§5.10) and **[real-airpods+driver]** (§5.11–§5.12) cases are the deferred human smoke test — record them when suitable hardware is available. There are **no parked issues** in this milestone.

---

## 8. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- Diagnostics exports accumulate in `%LOCALAPPDATA%\PodBridge\diagnostics\`; delete them if you were only testing. Log files live in `%LOCALAPPDATA%\PodBridge\logs\` and are self-capped (~10 MB / 7 days) — no manual cleanup needed, but they can be deleted.
- If you set **Debug logging** on, toggle it back off to return to the quiet Information default.
- Leaving the Phase-6 advanced tier installed/uninstalled and test-signing on/off is a Phase-6 cleanup concern — see `docs/qa/phase-6-advanced-driver-anc.md` §8.
