# PodBridge — Phase 1 Manual Test Guide (Foundation & Pairing)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine; a few cases need real AirPods, most do not.

## 1. Title & Scope

This guide verifies **Phase 1 — Foundation & Pairing**. Phase 1 delivers:

- A **tray-resident, single-instance** WPF app (start-to-tray, no startup window, no focus steal).
- **Live AirPods connect/disconnect status** in the tray (via WinRT).
- **Pairing guidance**: a one-time first-run notification and a `Pair / Reconnect` deep-link into Windows Bluetooth settings.
- **Graceful no-radio handling** (`Bluetooth unavailable`, never a crash).
- A green **Verify** gate and **CI** on `main`, built on the Microsoft.Extensions.Hosting generic host with a Core↔Windows DI composition root.

Phase 1 is **Tier 1: driver-free and requires no administrator rights** — the app runs `asInvoker` and must never trigger a UAC prompt.

Out of scope here: battery %, ear-detection play/pause, codec/mic policy, ANC/gestures (later phases).

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**) — required by the `net10.0-windows10.0.22621.0` target (AAC A2DP + WinRT BLE APIs).
- **.NET 10 SDK** installed (verified present: **10.0.301**; any `10.0.x` is fine). `dotnet` must be on `PATH`.
- **No administrator rights needed** — Tier 1 is driver-free and the manifest is `requestedExecutionLevel asInvoker`.
- Run **all** commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector` (the solution `PodBridge.slnx` and `build/verify.ps1` are referenced by relative path).
- For the repo/GitHub check (§5.11): the [`gh`](https://cli.github.com) CLI, authenticated against `bhemsen/PodBridge`.
- **For hardware feature tests only (§5.12–5.13):** a pair of AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max) and a working Bluetooth radio. *Not needed for build/verify* — the 28 unit tests use device-independent fakes.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run

Run each command from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet --version` | Prints `10.0.301` (any `10.0.x` SDK is fine). |
| 2a | `dotnet restore PodBridge.slnx` | `Alle Projekte sind für die Wiederherstellung auf dem neuesten Stand.` / `All projects are up-to-date for restore.` — no errors. |
| 2b | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` with **0 warnings / 0 errors**. Builds Core, Windows, App, and Core.Tests. |
| 3 | `dir src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe` | The single TFM folder is `net10.0-windows10.0.22621.0` and `PodBridge.App.exe` (~162 KB) exists inside it. |
| 5 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | **No window and no console output**; a PodBridge tray icon appears with a first-run pairing toast. |

> **Step 4** (the Verify gate) and **step 6** (stop the app) are covered below: step 4 in **§4 Automated checks**, step 6 under **Stop cleanly**.

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Launching (important):** it's a **WinExe (WPF) start-to-tray** app — launching from a terminal returns immediately and prints nothing. Do **not** wait on it; it stays resident. The only UI surface is the tray icon (click the up-arrow overflow if hidden). You may also just double-click the exe in Explorer.

**Stop cleanly (step 6):** right-click the tray icon → **Exit** (tears down the host, removes the tray icon, releases the single-instance mutex). Fallback: `taskkill /IM PodBridge.App.exe` or Task Manager → End task.

> **Gotchas:** (a) The TFM folder name `net10.0-windows10.0.22621.0` is long — only `PodBridge.App` outputs under it; Core/Tests build to plain `net10.0`. (b) Single-instance guard: a named mutex allows one instance per user session — a second launch shows `PodBridge is already running.` and exits. (c) `asInvoker`: launch must raise **no UAC prompt** — an elevation prompt means something is wrong.

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
- `== test ==` → `Bestanden!` / `Passed!` with **erfolgreich: 28, gesamt: 28** (28 passed, 0 failed, 0 skipped)

This single command covers **VERIFY-GREEN** and the two unit-test items **UNIT-CONNECT-DISCONNECT** and **UNIT-STATUS-PHRASES** (the `ConnectionStatusText` mapping and the fake-monitor connect→`Connected`/disconnect→`Disconnected` test).

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build`, relying on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail.

### 4.2 Static / repo inspections

Building does **not** by itself confirm solution membership, the docs, the manifest, or CI — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **SOLUTION-STRUCTURE** | `type PodBridge.slnx` | References `PodBridge.App`, `PodBridge.Windows`, `PodBridge.Core`, and `PodBridge.Core.Tests`; restore/build succeed. |
| **INTERFACES-DOCS** | `dir src\PodBridge.Core\Bluetooth\IConnectionMonitor.cs src\PodBridge.Windows\WinRtConnectionMonitor.cs` then `findstr /C:"IConnectionMonitor" /C:"WinRtConnectionMonitor" docs\architecture.md` | Both types exist in their projects; `docs/architecture.md` names `IConnectionMonitor` (Core) and `WinRtConnectionMonitor` (Windows). |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker" uiAccess="false"`; **no** `requireAdministrator` / `highestAvailable`. |
| **CI-CONFIG** | Inspect `.github\workflows\ci.yml` (or `gh workflow view CI --repo bhemsen/PodBridge`) | `runs-on: windows-latest`; `on:` push (branch `main`) **and** pull_request; steps include checkout, setup-dotnet `10.0.x`, `dotnet restore PodBridge.slnx`, and `powershell -NoProfile -File build/verify.ps1`. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --workflow CI --branch main --limit 1` | Latest CI run on `main` has conclusion `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown. The **Needs** tag tells you what a case requires so you can batch the no-hardware cases first.

**Reference — exact UI strings:**
- Status phrases: `Connected` · `Disconnected` · `No AirPods paired` · `Bluetooth unavailable` · `—` (unknown)
- Tray tooltip: `PodBridge` or `PodBridge — <status>`
- Context menu (top→bottom): `Status: —` (disabled) · `Pair / Reconnect` · `Open Bluetooth settings` · `Exit`
- Second-instance dialog body: `PodBridge is already running.`
- First-run toast: title `Pair your AirPods`, body `No AirPods are paired yet. Use "Pair / Reconnect" to add them in Windows Bluetooth settings.`
- First-run marker: `%LOCALAPPDATA%\PodBridge\first-run-guidance.marker`

---

### 5.1 Start-to-tray without stealing focus
- **Needs:** app only (no AirPods).
- **Action:** With no PodBridge running, click into another window (e.g. Notepad) to give it focus, then launch `PodBridge.App.exe`.
- **Expected:** No application window opens; focus **stays** on the previously-focused window; a PodBridge tray icon appears with tooltip `PodBridge` (or `PodBridge — <status>`).
- **Maps to:** LAUNCH-TO-TRAY (issue #2; Verify bullet 4a)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.2 No UAC / admin prompt on launch
- **Needs:** app only, as a standard (non-admin) user.
- **Action:** As a standard (non-admin) Windows user, launch `PodBridge.App.exe`.
- **Expected:** **No** UAC elevation prompt; the app runs as a normal, non-elevated process. (Static counterpart ASINVOKER-MANIFEST is checked in §4.2.)
- **Maps to:** NO-ADMIN-PROMPT (issue #2; Verify bullet 10b)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.3 Single instance (second launch refused)
- **Needs:** app only.
- **Action:** With PodBridge already running, launch `PodBridge.App.exe` a second time.
- **Expected:** A message box titled `PodBridge` with body `PodBridge is already running.` appears; the second process exits; exactly **one** `PodBridge.App` process and one tray icon remain.
- **Maps to:** SINGLE-INSTANCE (issue #2; Verify bullet 4b)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.4 Tray context menu — four items, correct order
- **Needs:** app only.
- **Action:** Right-click the tray icon to open the context menu; read the items top to bottom.
- **Expected:** A **disabled** status line `Status: <phrase>` (initially `Status: —`), then `Pair / Reconnect`, then `Open Bluetooth settings`, then `Exit` — exact labels, with separators.
- **Maps to:** MENU-ITEMS (issue #5; Verify bullet 5)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.5 "Open Bluetooth settings" deep-link
- **Needs:** app only.
- **Action:** Right-click the tray icon → `Open Bluetooth settings`.
- **Expected:** Windows Settings opens on the **Bluetooth & devices / Bluetooth** page (`ms-settings:bluetooth`). *(If the URI can't launch, a `PodBridge` warning box `Could not open Windows Bluetooth settings.` appears instead of a crash.)*
- **Maps to:** OPEN-BT-SETTINGS (issue #5)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.6 "Pair / Reconnect" deep-link (no AirPods paired)
- **Needs:** app only (no AirPods paired).
- **Action:** With no AirPods paired, right-click the tray icon → `Pair / Reconnect`.
- **Expected:** Windows Settings opens on the Bluetooth page (`ms-settings:bluetooth`).
- **Maps to:** PAIR-RECONNECT-DEEPLINK (issue #7; Verify bullet 7)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.7 One-time first-run notification
- **Needs:** app only (no AirPods paired).
- **Action:** Delete `%LOCALAPPDATA%\PodBridge\first-run-guidance.marker`, ensure **no AirPods paired**, launch PodBridge. Observe the toast; confirm the marker file is created; then Exit and relaunch.
- **Expected:** A Windows notification titled `Pair your AirPods` with body `No AirPods are paired yet. Use "Pair / Reconnect" to add them in Windows Bluetooth settings.` shows **once**; the marker file `%LOCALAPPDATA%\PodBridge\first-run-guidance.marker` is created; on relaunch the toast does **not** reappear. (Status line shows `Status: No AirPods paired`.)
- **Maps to:** FIRST-RUN-NOTIFICATION (issue #7; Verify bullet 7)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.8 Generic host + DI composition root resolves
- **Needs:** app + repo checkout for the code review (no AirPods).
- **Action:** Review `CompositionRoot.BuildHost` / `App.OnStartup`, then launch `PodBridge.App.exe`. Confirm the Microsoft.Extensions.Hosting generic host builds and `IConnectionMonitor` resolves to `WinRtConnectionMonitor` (`_host.Services.GetRequiredService<IConnectionMonitor>()`), i.e. the app starts tray-resident with **no** DI-resolution exception.
- **Expected:** Host builds and starts; `IConnectionMonitor` is registered and resolves to the WinRT adapter; the app becomes tray-resident with live status wired through `TrayStatusController`; no DI resolution exception on startup.
- **Maps to:** GENERIC-HOST-DI (issue #2)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.9 No Bluetooth radio → "Bluetooth unavailable", no crash
- **Needs:** app + ability to disable the radio (no AirPods).
- **Action:** Disable/remove the Bluetooth radio (Device Manager, or a machine with no adapter), then launch PodBridge and open the tray menu.
- **Expected:** App launches and stays resident; status line `Status: Bluetooth unavailable`, tooltip `PodBridge — Bluetooth unavailable`; **no crash or error dialog**. (Re-enable the radio afterwards.)
- **Maps to:** NO-BT-RADIO (issues #6/#7; Verify bullet 8)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.10 Clean Exit — no lingering process
- **Needs:** app only.
- **Action:** Right-click the tray icon → `Exit`. Check the notification area and Task Manager / process list.
- **Expected:** Tray icon is removed; **no** `PodBridge.App` process remains (host stopped, mutex released, tray disposed). A fresh launch then works again.
- **Maps to:** EXIT-CLEAN (issue #5; Verify bullet 5)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.11 Research comment on issue #4 (WinRT connection detection)
- **Needs:** repo + `gh` CLI (no AirPods).
- **Action:** Run `gh issue view 4 --repo bhemsen/PodBridge --comments`. Read the research comment and cross-check the named WinRT API against `WinRtConnectionMonitor` in `PodBridge.Windows`.
- **Expected:** A structured comment exists with **Sources / Consensus / Disputes**, names the paired-device enumeration + connection-status-change WinRT API (e.g. `DeviceInformation` / `DeviceWatcher` + `BluetoothDevice.ConnectionStatusChanged`), the implementation matches the consensus, and issue #4 introduced **no** product-code edits.
- **Maps to:** RESEARCH-COMMENT (issue #4; Verify bullet 3)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.12 Live status: connect → disconnect → reconnect
- **Needs:** **real AirPods** + working Bluetooth radio.
- **Action:** On a real machine with paired AirPods: (1) connect them, open the tray menu; (2) disconnect (case closed / powered off / removed), re-open the menu; (3) reconnect. Do **not** restart the app between steps.
- **Expected:** Connected → status line `Status: Connected`, tooltip `PodBridge — Connected`. On disconnect → live change to `Status: Disconnected` / `PodBridge — Disconnected`. On reconnect → back to `Connected`. No app restart needed.
- **Maps to:** LIVE-STATUS (issues #6/#7; Verify bullet 6)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

### 5.13 Human QA gate (end-to-end consolidation)
- **Needs:** **real AirPods** + working Bluetooth radio.
- **Action:** On real hardware with real AirPods: (1) connect → confirm tray shows `Connected`; (2) disconnect → confirm live update to `Disconnected`; (3) with AirPods removed, use `Pair / Reconnect` → confirm Windows Bluetooth settings opens.
- **Expected:** Tray status tracks real connect/disconnect **live**, and the pairing deep-link opens `ms-settings:bluetooth` — all with **no elevation, no driver, no crash**.
- **Maps to:** HUMAN-QA-GATE (Verify bullet 11; milestone QA gate)

`[ ] PASS   [ ] FAIL`
Notes: ____________________________________________

---

## 6. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #1** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** Phase 1 QA is **accepted** — the Phase 1 spec is archived, roadmap links are backfilled, and **milestone #1 is closed**, which unblocks **Phase 2**.

---

## 7. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process:
  ```
  tasklist /FI "IMAGENAME eq PodBridge.App.exe"
  ```
  Expected: `Keine Aufgaben` / `No tasks are running` — i.e. no `PodBridge.App.exe` remains.
- If any process lingers: `taskkill /IM PodBridge.App.exe`.
- If you disabled the Bluetooth radio for §5.9, re-enable it.
