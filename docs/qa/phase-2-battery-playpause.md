# PodBridge — Phase 2 Manual Test Guide (Battery & auto play/pause)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine; the battery and play/pause cases need real AirPods, the build/verify and repo cases do not.

## 1. Title & Scope

This guide verifies **Phase 2 — Battery & auto play/pause** (milestone #2, issues #13–#19). Phase 2 adds PodBridge's first passive-BLE telemetry on top of the Phase-1 shell:

- A **WinRT `BluetoothLEAdvertisementWatcher`** scanner (`WinRtBleScanner`) that filters on Apple company id **`0x004C`** with **active scanning** — driver-free, `asInvoker`, no admin.
- A **clean-room Apple-Continuity parser** (`ContinuityParser`, Core) decoding the `0x004C` proximity message into left/right bud + case battery %, per-bud/case charging, and in-ear/out-of-ear.
- A **connection-gated `DeviceState` pipeline** (`DeviceStateTracker` / `IDeviceStateProvider`): tracks the single **strongest-RSSI** `0x004C` device **only while an AirPods device is connected** (Phase-1 `IConnectionMonitor`), and goes **stale after 30 s** with no fresh advertisement.
- A **tray battery line** — left bud, right bud, case %, a charging indicator, and an explicit **"unknown / out of range"** state.
- **Automatic play/pause** — removing a bud (first bud out) **pauses** active media; re-inserting **resumes**, but only media PodBridge itself paused — via the Windows media-session manager (GSMTC, `WindowsMediaController`).

Phase 2 stays **Tier 1: driver-free and requires no administrator rights** — the app runs `asInvoker` and must never trigger a UAC prompt. Company id `0x004C` identifies AirPods **on the advertisement (telemetry) path only**; Phase-1's name-based `IConnectionMonitor` connection detection is unchanged.

Out of scope here: negotiated-codec (AAC/SBC) + mic-mode display (Phase 3), mic-profile policy (Phase 4), packaging (Phase 5), ANC/gestures + the L2CAP driver (Phases 6–7), cryptographic device binding + multi-device disambiguation (Phase 8).

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**) — required by the `net10.0-windows10.0.22621.0` target (AAC A2DP + WinRT BLE APIs).
- **.NET 10 SDK** installed (verified present: **10.0.301**; any `10.0.x` is fine). `dotnet` must be on `PATH`.
- **No administrator rights needed** — Tier 1 is driver-free and the manifest is `requestedExecutionLevel asInvoker`.
- Run **all** commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector` (the solution `PodBridge.slnx` and `build/verify.ps1` are referenced by relative path).
- For the repo/GitHub checks (§4.2, §5.12–5.14): the [`gh`](https://cli.github.com) CLI, authenticated against `bhemsen/PodBridge`.
- **For hardware feature tests (§5.3–5.10, §5.17):** a pair of AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max) and a working Bluetooth radio, **paired in Windows**. *Not needed for build/verify/repo cases* — the 63 unit tests use device-independent fakes.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

---

## 3. Build & Run

Run each command from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet --version` | Prints `10.0.301` (any `10.0.x` SDK is fine). |
| 2a | `dotnet restore PodBridge.slnx` | `Alle Projekte sind für die Wiederherstellung auf dem neuesten Stand.` / `All projects are up-to-date for restore.` — no errors. |
| 2b | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` with **0 warnings / 0 errors**. Builds Core, Windows, App, and Core.Tests. |
| 3 | `dir src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe` | The single TFM folder is `net10.0-windows10.0.22621.0` and `PodBridge.App.exe` exists inside it. |
| 4 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | **No window and no console output**; a PodBridge tray icon appears. On first run with no AirPods paired, the Phase-1 pairing toast still shows. |

> The **Verify gate** is covered in **§4 Automated checks**; **stopping the app** is covered under **Stop cleanly** and **§7 Cleanup**.

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Launching (important):** it's a **WinExe (WPF) start-to-tray** app — launching from a terminal returns immediately and prints nothing. Do **not** wait on it; it stays resident. The only UI surface is the tray icon (click the up-arrow overflow if hidden). You may also just double-click the exe in Explorer.

**Stop cleanly:** right-click the tray icon → **Exit** (tears down the host — which stops the BLE scanner and disposes the pipeline — removes the tray icon, and releases the single-instance mutex). Fallback: `taskkill /IM PodBridge.App.exe` or Task Manager → End task.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt** — an elevation prompt means something is wrong. (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) With no AirPods connected the battery line reads `Battery: unknown / out of range` — that is correct (the connection gate), not a failure.

---

## 4. Automated checks (machine-verified baseline — do these first)

These should already pass. All commands run from repo root.

### 4.1 Verify gate (build + analyzers + format + tests)

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** three sections print and **exit code 0**:

- `== build (Release) ==` → 0 warnings / 0 errors
- `== format --verify-no-changes ==` → silent (clean, no changes)
- `== test ==` → `Bestanden!` / `Passed!` with **erfolgreich: 63, gesamt: 63** (63 passed, 0 failed, 0 skipped)

This single command covers the machine-verifiable Verification items: the `ContinuityParser` fixture decode (left/right/case battery, charging, in-ear, incl. flipped and non-flipped frames), the `DeviceStateTracker` **connection-gate** test (live while connected, no live battery while disconnected) and its **30 s staleness** transition, and the `AutoPlayPauseEngine` tests (**pause on first bud out**, **resume on re-insert**, **never resume user-paused media**, **no calls while disconnected**).

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build`, relying on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail.

### 4.2 Static / repo inspections

Building does **not** by itself confirm the wiring, the docs, the manifest, or the research artefacts — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **NEW-COMPONENTS-EXIST** | `dir src\PodBridge.Core\Protocol\ContinuityParser.cs src\PodBridge.Core\Bluetooth\DeviceStateTracker.cs src\PodBridge.Core\Bluetooth\IDeviceStateProvider.cs src\PodBridge.Core\Media\AutoPlayPauseEngine.cs src\PodBridge.Core\Media\IMediaController.cs src\PodBridge.Windows\WinRtBleScanner.cs src\PodBridge.Windows\WindowsMediaController.cs` | All seven files exist. |
| **DI-WIRING** | `findstr /C:"IBleScanner" /C:"IMediaController" src\PodBridge.Windows\ServiceCollectionExtensions.cs` then `findstr /C:"IDeviceStateProvider" /C:"AutoPlayPauseEngine" src\PodBridge.App\CompositionRoot.cs` | Windows binds `IBleScanner`→`WinRtBleScanner` and `IMediaController`→`WindowsMediaController`; the App composition root registers `IDeviceStateProvider`→`DeviceStateTracker` and `AutoPlayPauseEngine`. The Phase-1 `IConnectionMonitor` binding is reused unchanged. |
| **ARCHITECTURE-DOC** | `findstr /C:"ContinuityParser" /C:"DeviceStateTracker" /C:"AutoPlayPauseEngine" /C:"IConnectionMonitor" docs\architecture.md` | `docs/architecture.md` names the new Phase-2 components and states Phase-2 telemetry + play/pause are **gated on Phase-1's `IConnectionMonitor`**. |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker" uiAccess="false"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 2). |
| **RESEARCH-COMMENTS** | `gh issue view 13 --repo bhemsen/PodBridge --comments`, then the same for `15` and `17` | Each carries a structured `## Research: …` comment (Sources / Consensus / Disputes). Also `dir docs\research\continuity-parser.md docs\research\ble-watcher.md docs\research\media-control.md`. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 1` | Latest CI (`verify`) run on `main` has conclusion `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown. The **Needs** tag tells you what a case requires so you can batch the no-hardware cases first.

**Reference — exact UI strings:**
- Context menu (top→bottom): `Status: —` (disabled) · `Battery: —` (disabled) · *(separator)* · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · `Exit`
- Status phrases: `Connected` · `Disconnected` · `No AirPods paired` · `Bluetooth unavailable` · `—` (unknown)
- Battery line, live: `Battery: L 80% · R 70%⚡ · Case 50%` — per component `L`/`R`/`Case`, `<n>%` (10% steps), charging mark `⚡`, separator ` · ` (space · middle-dot · space)
- Battery line, a component with no reading: `Battery: L unknown · R 70% · Case 50%`
- Battery line, not live (disconnected / stale / no AirPods): `Battery: unknown / out of range`
- Tray tooltip: `PodBridge` initially, then `PodBridge — <status> · <battery>` (e.g. `PodBridge — Connected · L 80% · R 70%⚡ · Case 50%`)

---

### 5.1 Build & run to tray, no UAC
- **Needs:** app only (no AirPods).
- **Action:** As a standard (non-admin) user, launch `PodBridge.App.exe`.
- **Expected:** App starts straight to the **tray** with **no UAC / admin prompt** and no window/console; the tray icon appears. No driver is installed.
- **Maps to:** issue #19; spec Verification "runs without elevation (`asInvoker`)".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.2 Context menu — battery line present, correct order
- **Needs:** app only.
- **Action:** Right-click the tray icon; read the items top to bottom.
- **Expected:** A **disabled** `Status: <phrase>` line, then a **disabled** `Battery: <phrase>` line, then a separator, `Pair / Reconnect`, `Open Bluetooth settings`, a separator, and `Exit`. With no AirPods connected the battery line reads `Battery: unknown / out of range`.
- **Maps to:** issue #19 (tray battery display).

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.3 Battery display — left/right/case % with charging (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** Connect your AirPods to this PC. Open the tray context menu (and hover the icon for the tooltip). Then put the buds in the case / put the case on charge.
- **Expected:** The battery line shows **all three** components, e.g. `Battery: L 80% · R 70% · Case 50%`. A **⚡** appears next to any component that is charging (e.g. `Case 50%⚡`) and clears when charging stops. The tooltip reads `PodBridge — Connected · L 80% · R 70% · Case 50%`.
- **Maps to:** issue #14/#19; spec Verification "tray shows correct left/right bud + case battery with charging indicators".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.4 10% granularity and per-component "unknown" (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** With AirPods connected, let the buds/case charge or discharge over time and re-check the battery line.
- **Expected:** Values change in **10% steps** (Apple's coarse granularity, 0–100%). A component with no reading shows the literal word `unknown` (the `0xF` sentinel) — e.g. `Battery: L unknown · R 70% · Case 50%` — never `0%` and never a fabricated number.
- **Maps to:** issue #14; spec Prior decision "10% granularity + explicit unknown".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.5 Auto play/pause — pause on FIRST bud out (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** With both AirPods in your ears, start media (browser/Spotify) playing through them. Remove **one** bud.
- **Expected:** Media **pauses within ~2 s** of removing the first bud (the trigger is *both buds in* → *not both in*).
- **Maps to:** issue #18; spec Verification "removing a bud pauses active media within a couple of seconds".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.6 Auto play/pause — resume on re-insert (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** Continuing from §5.5 (PodBridge paused the media), put the removed bud back in your ear so **both** buds are in.
- **Expected:** Media **resumes** automatically.
- **Maps to:** issue #18; spec Verification "re-inserting resumes it".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.7 Never auto-resume user-paused media (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** Start media, then **pause it yourself** (keyboard/app). Now remove and re-insert a bud.
- **Expected:** PodBridge does **not** start playback — it only resumes media *it* paused ("paused-by-us"). Removing a bud while already user-paused also fires no pause/play.
- **Maps to:** issue #18; spec Verification "media the user paused is not auto-resumed".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.8 Connection gate — nothing tracked while not connected (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio (ideally a second pair of AirPods advertising nearby).
- **Action:** Disconnect your AirPods but leave them powered/advertising nearby (case open, or in your ears but disconnected). If possible, have another AirPods advertising in the room. Play some media on the PC speakers.
- **Expected:** The battery line stays `Battery: unknown / out of range`, and **no play/pause fires** — even though `0x004C` advertisements are in range. Telemetry is gated on the Phase-1 **connected** state; only *your connected* AirPods drive the tray.
- **Maps to:** issue #14/#18; spec Verification "with no AirPods connected, nearby advertisements do not show live battery and do not drive play/pause".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.9 Staleness — out of range → "unknown / out of range" (real AirPods)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** With AirPods connected and battery showing, take them **out of BLE range** (or close the case lid so proximity broadcasts stop). Wait past the staleness timeout.
- **Expected:** After the **~30 s** staleness timeout the battery line shows exactly `Battery: unknown / out of range`. **No crash**, and **no stale value is left showing as if live**. Bringing them back in range restores live values.
- **Maps to:** issue #14; spec Prior decision "battery/in-ear go stale after a 30 s timeout".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.10 Company-id identification / non-Apple ignored (sanity)
- **Needs:** app + (optionally) other BLE devices nearby.
- **Action:** During §5.3–5.9, watch that only Apple AirPods ever appear as battery. Keep non-Apple BLE devices (mouse, keyboard, fitness band, other-brand earbuds) around.
- **Expected:** Only Apple `0x004C` advertisements are decoded; no non-Apple BLE device ever appears as AirPods battery. (Machine counterpart: covered by the `ContinuityParser`/`DeviceStateTracker` unit tests in §4.1 — non-Apple advertisements are ignored.)
- **Maps to:** issue #14; spec Verification "a `0x004C` AirPods advertisement is picked up while non-Apple BLE advertisements are ignored".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.11 Tooltip composition (status · battery)
- **Needs:** app (real AirPods to see the live form).
- **Action:** Hover the tray icon before connecting, then after connecting AirPods.
- **Expected:** Before any update the tooltip is `PodBridge`; once wired it is `PodBridge — <status> · <battery>` — e.g. `PodBridge — Disconnected · unknown / out of range`, then `PodBridge — Connected · L 80% · R 70% · Case 50%`. The status and battery halves update independently (neither clobbers the other).
- **Maps to:** issue #19 (tray wiring).

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.12 Research comment on issue #13 (Continuity 0x004C byte layout)
- **Needs:** repo + `gh` CLI (no AirPods).
- **Action:** `gh issue view 13 --repo bhemsen/PodBridge --comments`. Read the `## Research: Apple-Continuity 0x004C proximity message` comment; cross-check its consensus against `src\PodBridge.Core\Protocol\ContinuityParser.cs`.
- **Expected:** Structured comment (Sources ≥3 / Consensus / Disputes) exists; the parser's offsets/bits reflect it (type `0x07`, primary/secondary→left/right flip, in-ear XOR, `0xF` unknown sentinel, model-id table); no product-code edits in issue #13 itself. `docs/research/continuity-parser.md` mirrors it.
- **Maps to:** issue #13; spec Verification "research comment as QA artefact".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.13 Research comment on issue #15 (WinRT advertisement watcher)
- **Needs:** repo + `gh` CLI.
- **Action:** `gh issue view 15 --repo bhemsen/PodBridge --comments`. Cross-check against `src\PodBridge.Windows\WinRtBleScanner.cs`.
- **Expected:** `## Research: WinRT BluetoothLEAdvertisementWatcher` comment exists; the scanner uses **active scanning**, a `0x004C` manufacturer-data filter **plus** a handler-side company-id re-check, and needs **no capability/admin** for the unpackaged `asInvoker` build. `docs/research/ble-watcher.md` mirrors it.
- **Maps to:** issue #15; spec Verification "research comment as QA artefact".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.14 Research comment on issue #17 (GSMTC media control)
- **Needs:** repo + `gh` CLI.
- **Action:** `gh issue view 17 --repo bhemsen/PodBridge --comments`. Cross-check against `src\PodBridge.Windows\WindowsMediaController.cs`.
- **Expected:** `## Research: Windows media-session control` comment exists; the controller uses **GSMTC** `GetCurrentSession()`, `TryPauseAsync`/`TryPlayAsync`, and reads `PlaybackStatus` (so it pauses only when playing and resumes only what it paused), needs **no admin/driver**, and degrades gracefully. `docs/research/media-control.md` mirrors it.
- **Maps to:** issue #17; spec Verification "research comment as QA artefact".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.15 No elevation at runtime
- **Needs:** app only.
- **Action:** With PodBridge running, open Task Manager → Details, find `PodBridge.App.exe`; confirm it is not elevated (no admin was ever requested). No driver/INF/`pnputil` step occurred.
- **Expected:** Process runs as a normal (Medium-IL) user process — Tier 1, driver-free.
- **Maps to:** spec Verification "the process runs without elevation (`asInvoker`)".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.16 Honesty — no Apple-parity sound claim
- **Needs:** app / repo (sanity).
- **Action:** Read every user-facing string surfaced in Phase 2 (tray menu, tooltip, notifications).
- **Expected:** No string claims Apple-identical/parity sound (constitution honesty principle). Phase 2 has no codec UI yet — battery + status text only.
- **Maps to:** constitution "honest audio surface".

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.17 Human QA gate (end-to-end consolidation)
- **Needs:** **real AirPods** + Bluetooth radio.
- **Action:** On real hardware: (1) connect AirPods → confirm the tray battery line shows live L/R/case %; (2) charge a component → confirm the `⚡` mark; (3) play media, remove one bud → confirm pause ~2 s, re-insert → resume; (4) disconnect → confirm the battery line goes `unknown / out of range` and no play/pause fires; (5) out of range ~30 s → confirm `unknown / out of range`, no crash.
- **Expected:** Battery, charging, auto play/pause, the connection gate, and staleness all behave as above — with **no elevation, no driver, no crash**.
- **Maps to:** milestone #2 QA gate; spec Verification human-QA items.

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

---

### Known residual limitations (documented, **not** defects — do not reject Phase 2 for these)

- **Crowded-room cross-talk.** While your AirPods are connected, the tracker picks the **strongest-RSSI** `0x004C` advertisement. A *nearer stranger's* AirPods could in principle be tracked (Apple rotates random addresses, so an advertisement can't be cryptographically bound to *your* buds). The connection gate removes the empty-room case; cryptographic disambiguation is **Phase 8**.
- **Single active media session.** Auto play/pause targets the **current** GSMTC session only; with several apps playing at once it may pause/resume the "wrong" one.
- **In-ear source.** In-ear/out-of-ear comes from the **BLE advertisement proximity message**, not the L2CAP AAP opcode (which needs the Phase-6/7 driver); lid/in-ear bits are fully trustworthy only on in-case broadcasts.

---

## 6. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #2** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** Phase 2 QA is **accepted** — the Phase 2 spec (`docs/specs/archive/spec-battery-status.md`) is archived, roadmap links are updated, and **milestone #2 is closed**, which unblocks **Phase 3 (Audio transparency)**.

---

## 7. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process:
  ```
  tasklist /FI "IMAGENAME eq PodBridge.App.exe"
  ```
  Expected: `Keine Aufgaben` / `No tasks are running` — i.e. no `PodBridge.App.exe` remains.
- If any process lingers: `taskkill /IM PodBridge.App.exe`.
- If you took AirPods out of range or disconnected them for §5.8/§5.9, reconnect them when done.
