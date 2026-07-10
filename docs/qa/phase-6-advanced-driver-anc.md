# PodBridge — Phase 6 Manual Test Guide (Advanced tier: KMDF L2CAP driver + ANC)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine. Phase 6 is the **OPTIONAL, opt-in advanced tier** — a separately-installed kernel driver that unlocks noise-control switching; it is never installed or elevated by default. Each case is tagged **[machine]** (no AirPods, no driver — repo/CI/unit checks + the driver-absent graceful-degradation path) or **[real-hardware+driver]** (needs the driver actually installed and loaded, plus AirPods, on a real Windows 11 box) so the no-hardware cases can be batched first. The **[real-hardware+driver]** cases each require explicit, honestly-warned user/admin actions that lower the machine's driver-security bar; none of them are ever performed silently by PodBridge.

## 1. Title & Scope

This guide verifies **Phase 6 — Advanced tier: KMDF L2CAP driver + AAP writes** (milestone #6), the first slice of the **opt-in advanced tier** (Tier 2). Implemented issues (all merged, **no parked issues** in this milestone): **#39** research (AAP noise-control byte format), **#40** research (KMDF/L2CAP/INF/`pnputil`/signing reality), **#41** Core `AapProtocol` + `IAapTransport`, **#42** KMDF L2CAP bridge driver (`driver/PodBridgeAAP`, compile-only CI), **#43** `DriverAapTransport` (`PodBridge.Windows`), **#44** "Noise control" tray submenu (Off / Noise Cancellation / Transparency / Adaptive, optimistic-set / echo-confirm / timeout-revert), **#45** opt-in advanced-tier install + signing / test-mode honesty UX. Phase 6 adds:

- An **OPTIONAL C/KMDF L2CAP bridge driver** in `driver/PodBridgeAAP` that opens the cleartext AAP control channel (Classic-Bluetooth **L2CAP PSM `0x1001`**) to a connected AirPods device and exposes a user-mode WDF **device interface**. It **ships and installs separately** from the app (its own INF + `pnputil`) and is **never bundled in the Phase-5 MSIX**. It builds compile-only in CI; loading it is a manual, opt-in, real-hardware step.
- A **clean-room `AapProtocol` module** and the **`IAapTransport`** interface in `PodBridge.Core` (OS-free): it builds the 16-byte plaintext handshake, the "set specific features" (Adaptive-unlock) frame, the request-notifications frame, and the 11-byte noise-control **set** frame `04 00 04 00 09 00 0D [mode] 00 00 00`, and parses the identical inbound **echo/notification** frame. Every constant carries a documented-fact citation to `docs/research/aap-anc-protocol.md`.
- **`DriverAapTransport`** (`PodBridge.Windows`) implementing `IAapTransport` over the driver's device interface (open / send / inverted-call receive loop). It is the **only** component that talks to the driver; it **probes for the driver at startup and reports `IsAvailable = false` when absent**, so Core/App never touch the driver.
- The **Core `NoiseControlController`** (optimistic-set / echo-confirm / timeout-revert) and the **"Noise control" tray submenu** wired via `TrayNoiseControlController`: a set is applied optimistically, the SET frame is sent, and the change is **confirmed** only when the AirPods echo the same mode back within the 2-second window — otherwise the optimistic change is **reverted** and an honest toast is shown. **Adaptive** is gated on the connected model (AirPods Pro 2 reference).
- The **opt-in enablement UX** (issue #45): a driver-absent "Enable advanced tier…" affordance that shows an honest security warning naming **both** x64 load requirements, then — only on explicit confirmation — launches the **separate, elevated** install step (`install-advanced-tier.ps1` → trust the self-signed test cert + `pnputil /add-driver … /install`). The app itself stays `asInvoker` and **never runs `bcdedit`**.

Phase 6 keeps **Tier 1 fully intact and driver-free.** With the driver **absent** (the default), every Phase 1–5 feature still works, the .NET Verify suite passes, and the "Noise control" submenu is disabled with an honest explanation + the opt-in affordance — **no crash, no elevation.** The advanced tier is invasive only when the user explicitly opts in.

> **Honest driver-signing reality (read this first).** Loading a **test-signed** KMDF driver on **64-bit Windows** requires **TWO machine-wide security changes, and neither alone is enough**: (1) **enabling test-signing mode** (`bcdedit /set testsigning on` + reboot) — a manual user action PodBridge **never** performs (`bcdedit` is on the project deny-list); and (2) **trusting the self-signed test certificate** by importing it into **both** the machine's **Trusted Root Certification Authorities** and **Trusted Publishers** stores (skipping the root import gives the classic "a certificate chain … terminated in a root certificate which is not trusted" load failure). This driver is **TEST-signed with a locally-generated self-signed certificate** — it is **NOT** Microsoft-signed / attestation-signed. The production path (an EV code-signing certificate + Microsoft Partner Center) is **DEFERRED / out of scope** for Phase 6 (see §6). Do not fail any case for "it needs test-signing + a trusted cert to load" — that is the documented, honest reality.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

Out of scope here: gesture / stem-press remap (Phase 7 — reuses this phase's `IAapTransport`/driver); conversation awareness, device rename, and any AAP command beyond noise control (later phases); broad model/firmware coverage + the full Adaptive matrix + diagnostics (Phase 8 — this phase targets the AirPods Pro 2 reference model and gates Adaptive on what the connected model reports); a Microsoft-attestation-signed public release (deferred). Battery / play-pause (Phase 2), codec + mic-mode display (Phase 3), the mic-profile policy (Phase 4), and packaging/About (Phase 5) are unchanged Tier-1 paths — this phase moves none of them onto the L2CAP channel.

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** for any **[machine]** case — Tier 1 is driver-free and the app manifest is `asInvoker`. The **[real-hardware+driver]** cases require admin for the explicit, opt-in install steps only.
- Run all repo commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/CI checks (§4.2, §5.1, §5.3): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the driver-build case:** Visual Studio (Build Tools) with the **Desktop development with C++** workload + the **Windows Driver Kit** component; the WDK/SDK are restored from the `Microsoft.Windows.WDK.x64` NuGet package. The `dotnet` CLI does **not** drive WDK builds — use MSBuild / the shipped `build-testsign.ps1`.
- **For the [real-hardware+driver] cases (§5.7–§5.13):** a **dedicated / disposable test machine you are willing to put into test-signing mode** (it will show the "Test Mode" desktop watermark), real **AirPods Pro 2** (the reference model; Adaptive is Pro 2 only), a working Bluetooth radio, and admin rights on that box. *Not needed for the machine cases* — those are repo/CI/unit-test checks + the driver-absent path.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run (dev build, driver absent)

The dotnet **Verify** gate builds and tests the **managed** app the normal way; it does **not** build the KMDF driver (the driver `.vcxproj` needs the WDK and is intentionally kept **out of `PodBridge.slnx`** — it builds only via `driver/PodBridgeAAP/build-testsign.ps1` or the separate Driver CI workflow). Run each from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt** — including when you click "Enable advanced tier…", which only elevates the *launched installer* after an explicit warning, never PodBridge itself. (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) On a dev build the driver is **absent**, so the "Noise control" submenu is **disabled** and shows the driver-absent block — this is the correct Tier-1 default, not a failure.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from the repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` across **two** test projects: **`PodBridge.Core.Tests` erfolgreich: 154, gesamt: 154** and **`PodBridge.Windows.Tests` erfolgreich: 18, gesamt: 18** (172 passed, 0 failed, 0 skipped). This includes the **device-independent Phase-6 gates**, all driven by fakes with **no driver and no hardware**:

- **`AapProtocolTests`** — the clean-room byte format: the handshake, set-specific-features, and request-notifications frames encode to the researched bytes; `BuildSetNoiseControl` produces `04 00 04 00 09 00 0D [mode] 00 00 00` for Off/ANC/Transparency/Adaptive (`01`/`02`/`03`/`04`); and `TryParseNoiseControlNotification` round-trips the echo frame and rejects malformed / unknown-mode frames.
- **`NoiseControlControllerTests`** — a fake `IAapTransport` drives the optimistic-set → echo-confirm (`Confirmed`) path, the **timeout / mismatch → revert** (`RevertedOnTimeout`, state rolled back) path, and the **driver-absent** path (`Unavailable`, **nothing sent**, `ApplyTo` reports switching disabled).
- **`NoiseControlSupportTests`** — Adaptive is reported supported for **AirPods Pro 2 (and Pro 2 USB-C) only**, not other models.
- **`AdvancedTierInfoTests`** — the honesty invariants on the enable-flow copy: it names **both** load requirements (test-signing mode + test-cert trust), states the machine-wide trade-off, and makes **no** Microsoft-signed / production claim.
- **`AdvancedTierDocsTests`** — the same honesty invariants pinned on the shipped `docs/user/advanced-tier.md`.
- **`AdvancedTierInstallerTests`** (Windows) — the locate/launch decision at the Win32 seam via fakes: package-missing → `PackageMissing`, launch declined → `Cancelled`, launched → `Launched`; the app is never elevated.
- **`DriverAapTransportTests`** (Windows) — via a fake interop: graceful absence (`IsAvailable = false`, `ConnectAsync` a no-op), send, and the inverted-call receive loop surfacing frames as `PacketReceived`.

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail. **Verify does not build the driver** — that is the separate Driver CI job (§5.1).

### 4.2 Static / repo inspections — [machine]

The dotnet build does not exercise the driver, the driver's exclusion from the solution, the INF/scripts, or the research artefacts — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **CORE-TYPES-EXIST** | `dir src\PodBridge.Core\Protocol\AapProtocol.cs src\PodBridge.Core\Protocol\IAapTransport.cs src\PodBridge.Core\Protocol\NoiseControlController.cs src\PodBridge.Core\Protocol\NoiseControlSupport.cs src\PodBridge.Core\Models\NoiseControlMode.cs` | All exist. |
| **ADVANCEDTIER-TYPES-EXIST** | `dir src\PodBridge.Core\AdvancedTier\IAdvancedTierInstaller.cs src\PodBridge.Core\AdvancedTier\AdvancedTierInfo.cs src\PodBridge.Core\AdvancedTier\AdvancedTierActionResult.cs` | All exist; the honest enable-flow copy lives in Core (`AdvancedTierInfo`), no UI dependency. |
| **WINDOWS-ADAPTERS-EXIST** | `dir src\PodBridge.Windows\DriverAapTransport.cs src\PodBridge.Windows\AdvancedTierInstaller.cs src\PodBridge.Windows\Interop\AdvancedTierInstallInterop.cs` | All exist. |
| **CORE-IS-OS-FREE** | `findstr /I /C:"DllImport" /C:"P/Invoke" /C:"CreateFile" src\PodBridge.Core\Protocol\AapProtocol.cs src\PodBridge.Core\Protocol\NoiseControlController.cs` | **No match** — Core carries no P/Invoke; all Win32/COM lives in `PodBridge.Windows`. |
| **CLEAN-ROOM-CITATIONS** | `type src\PodBridge.Core\Protocol\AapProtocol.cs` | Every opcode/constant (PSM `0x1001`, data header `04 00 04 00`, opcode `0x0009` + identifier `0x0D`, mode bytes, handshake, set-specific-features, request-notifications) carries a comment citing `docs/research/aap-anc-protocol.md`; no GPL source / verbatim doc prose copied. |
| **DRIVER-FILES-EXIST** | `dir driver\PodBridgeAAP\Driver.c driver\PodBridgeAAP\Device.c driver\PodBridgeAAP\L2cap.c driver\PodBridgeAAP\Queue.c driver\PodBridgeAAP\Public.h driver\PodBridgeAAP\PodBridgeAAP.inf driver\PodBridgeAAP\PodBridgeAAP.vcxproj driver\PodBridgeAAP\build-testsign.ps1 driver\PodBridgeAAP\install-advanced-tier.ps1` | All exist. |
| **DRIVER-NOT-IN-SOLUTION** | `findstr /I "PodBridgeAAP driver" PodBridge.slnx` | **No match** — the KMDF driver is intentionally **not** in `PodBridge.slnx` (the managed Verify never tries to build it). |
| **INF-SHAPE** | `type driver\PodBridgeAAP\PodBridgeAAP.inf` | `Class=Bluetooth`, `ClassGuid={e0cbf06c-cd8b-4647-bb8a-263b43f0f974}`, `PnpLockdown=1`, KMDF (`KmdfLibraryVersion = 1.33`, coinstaller-free), binds by `BTHENUM\{36f88597-6bae-4e3d-a454-66c3d877f4ea}` (the custom PodBridge AAP service GUID); SDDL lets a non-elevated session open the interface (elevation is only for install). |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 6 — the driver install elevates a *separate* PowerShell step, never the app). |
| **APP-NEVER-RUNS-BCDEDIT** | `findstr /I /S "bcdedit" src\PodBridge.App src\PodBridge.Windows src\PodBridge.Core` | The only `bcdedit` mentions are in **honesty copy / comments** stating the user must run it themselves and PodBridge never does — there is **no** code path that executes `bcdedit`. |
| **DRIVER-NOT-IN-MSIX** | `findstr /I ".sys" packaging\PodBridge.Package\Package.appxmanifest packaging\PodBridge.Package\PodBridge.Package.wapproj` | **No match** — the app MSIX stays app-only; the driver ships separately (the `packaging.yml` `Assert app-only` step still guards this). |
| **RESEARCH-ARTEFACTS** | `gh issue view 39 --repo bhemsen/PodBridge --comments`, `... 40 ...`, then `dir docs\research\aap-anc-protocol.md docs\research\kmdf-l2cap.md` | #39 `## Research:` on the AAP noise-control byte format (handshake, notification-register, opcode `0x09`/id `0x0D`, Off/ANC/Transparency/Adaptive `01`–`04`, echo-confirm); #40 `## Research:` on `BRB_L2CA_OPEN_CHANNEL` PSM `0x1001`, the WDF interface + IOCTL/inverted-call I/O, INF + `pnputil`, and the **both-requirements** load story. Both docs present and reflected in the code. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 5` | The latest `CI` (Verify) run on `main` is `success`. The `Driver CI` / `KMDF driver compile-only (x64)` job (non-blocking) is present. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown.

**Reference — exact UI strings.**

- **Top-level tray context menu** (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · **`Microphone mode`** (submenu, Phase 4) · **`Noise control`** (submenu, Phase 6) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · `About PodBridge` · `Exit`
- **`Noise control`** submenu (top→bottom) — four checkable **radio** modes, then a driver-absent block that is **collapsed** while the driver is present:
  - `Off` · `Noise Cancellation` · `Transparency` · `Adaptive`
  - *(separator — shown only when the driver is absent)*
  - driver-absent explanation line (disabled): **`Requires the optional advanced tier (driver not installed)`**
  - **`Enable advanced tier…`**
- **Timeout/mismatch revert toast** — title **`Noise control`**, body **`Couldn't confirm the change with your AirPods — reverted.`**
- **"Enable advanced tier" warning dialog** — title **`Enable the advanced tier`**, OK/Cancel with a warning icon, body verbatim:
  > `Loading this driver on 64-bit Windows requires TWO machine-wide security changes, and it is NOT a Microsoft-signed driver:`
  >
  > `1. Test-signing mode — you must enable it yourself with "bcdedit /set testsigning on" and reboot. PodBridge never runs bcdedit for you.`
  > `2. Trusting a self-signed test certificate — the installer imports it into your machine's Trusted Root Certification Authorities and Trusted Publishers stores.`
  >
  > `Together these lower your machine's driver-security bar until you undo them. Both are opt-in and reversible, and every default (Tier-1) feature keeps working without them. Continue to the elevated installer?`
- **After launching the installer** — toast title `Enable the advanced tier`, body: **`The advanced-tier installer was started — approve the Windows admin prompt. When it finishes, enable test-signing yourself ("bcdedit /set testsigning on") and reboot. PodBridge re-checks for the driver on the next launch.`**
- **Driver package missing** — toast title `Enable the advanced tier`, body: **`The advanced-tier driver isn't on this PC. It ships separately from PodBridge (it is never bundled in the app). Opening the advanced-tier guide, which explains how to build or download and install it.`** (and the advanced-tier docs open).
- **Install / trust / sign commands** (exact):
  - Build + test-sign (normal shell, from `driver\PodBridgeAAP`): `.\build-testsign.ps1` → package + `PodBridgeTest.cer` under `driver\PodBridgeAAP\x64\Release` (cert subject `CN=PodBridge Test (AAP Driver)`).
  - Enable test-signing (elevated, **manual — PodBridge never does this**): `bcdedit /set testsigning on` then **reboot**.
  - Trust the self-signed cert (elevated): `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine root` **and** `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine trustedpublisher` (the installer's `Import-Certificate` into `LocalMachine\Root` + `LocalMachine\TrustedPublisher` is the equivalent).
  - Install the driver (elevated): `pnputil /add-driver PodBridgeAAP.inf /install`.
  - Or the one-shot opt-in install (self-elevates, single UAC — trusts the cert **and** `pnputil`-installs, but **never** `bcdedit`): `.\install-advanced-tier.ps1 -Action install`.
  - Uninstall (elevated): `.\install-advanced-tier.ps1 -Action uninstall` → `pnputil /delete-driver <oemNN.inf> /uninstall` + removes the cert; then optionally `bcdedit /set testsigning off` (reboot).

---

### 5.1 Driver compiles in CI (KMDF compile-only, non-blocking) — [machine]
- **Needs:** repo + `gh`.
- **Action:** Inspect the `Driver CI` workflow and its latest run. `type .github\workflows\driver-ci.yml`; then `gh run list --repo bhemsen/PodBridge --workflow "Driver CI" --limit 3` and `gh run view <id> --repo bhemsen/PodBridge`.
- **Expected:** The `Driver CI` workflow runs the job named **`KMDF driver compile-only (x64)`** on `windows-latest`, is **`continue-on-error: true`** (non-blocking — a driver compile break must never gate Verify or a merge), restores + builds `driver/PodBridgeAAP/PodBridgeAAP.vcxproj` (`Configuration=Release`, `Platform=x64`) via the NuGet WDK, and confirms `driver/PodBridgeAAP/x64/Release/PodBridgeAAP.sys` was produced. It only triggers on `driver/**` (and its own file) changes; it does **not** sign, load, or exercise hardware.
- **Maps to:** issue #42; spec Prior decision "CI adds a non-blocking, compile-only driver-build job"; research #40 (e).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 Driver is NOT in the managed solution; app stays app-only — [machine]
- **Needs:** repo.
- **Action:** `findstr /I "PodBridgeAAP driver" PodBridge.slnx` (expect no match); confirm the driver builds only via its own script/workflow; re-check the MSIX has no `.sys` (§4.2 **DRIVER-NOT-IN-MSIX**).
- **Expected:** `PodBridge.slnx` contains **no** driver project, so `dotnet build` / Verify never attempt a WDK build (they would fail without the WDK). The app MSIX remains **app-only** (no `.sys`) — the driver is a wholly separate, opt-in package. This is the structural guarantee that Tier 1 stays driver-free.
- **Maps to:** spec Outcome "ships and installs separately … never bundled in the Phase-5 MSIX"; constitution "MSIX cannot cleanly bundle a kernel driver".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Research comments (#39, #40) present + reflected in code — [machine]
- **Needs:** repo + `gh`.
- **Action:** `gh issue view 39 --repo bhemsen/PodBridge --comments` and `gh issue view 40 --repo bhemsen/PodBridge --comments`; cross-check against `src\PodBridge.Core\Protocol\AapProtocol.cs` and the driver + `install-advanced-tier.ps1`.
- **Expected:** #39 `## Research:` (Sources / Consensus / Disputes) fixes the AAP noise-control bytes (PSM `0x1001`; handshake; request-notifications; opcode `0x0009` + id `0x0D`; Off/ANC/Transparency/Adaptive `01`–`04`; echo-confirm; the Adaptive-unlock caveat; Pro 2 fw `7A305`) — reflected in `AapProtocol`. #40 `## Research:` fixes the KMDF/L2CAP mechanics (`BRB_L2CA_OPEN_CHANNEL` on PSM `0x1001`, the WDF device interface + IOCTL/inverted-call I/O, INF + `pnputil`, and the **both-requirements** x64 load story) — reflected in the driver + install flow. `docs/research/aap-anc-protocol.md` and `docs/research/kmdf-l2cap.md` present.
- **Maps to:** issues #39/#40; spec Verification (research comments = QA artefacts).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Graceful degradation — driver ABSENT: submenu disabled + honest affordance, Tier-1 unaffected — [machine]
- **Needs:** the dev build (§3) or the installed MSIX, on a machine **without** the driver (the Tier-1 default).
- **Action:** Launch PodBridge with no driver installed. Right-click the tray → open **`Noise control`**. Then exercise a Tier-1 feature (open `Microphone mode`, click `Refresh audio status`). Open Task Manager → Details and confirm `PodBridge.App.exe` is not elevated.
- **Expected:** Inside `Noise control` the four modes `Off` / `Noise Cancellation` / `Transparency` / `Adaptive` are **disabled**, and the driver-absent block is **visible**: the line **`Requires the optional advanced tier (driver not installed)`** and **`Enable advanced tier…`**. Nothing is sent, **no elevation** happens, and there is **no crash**. Every Tier-1 feature (battery, play/pause, codec/mic display, mic-profile policy) still works and the .NET Verify suite passes with the driver absent (§4.1). This is the constitution's graceful-degradation gate.
- **Maps to:** issue #43/#44; spec Outcome "with the driver absent, every Tier-1 feature still works … no crash, no elevation" + Verification "driver uninstalled".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 "Enable advanced tier…" warning is honest about BOTH load requirements — [machine (strings unit-tested) + visual]
- **Needs:** running app with the driver **absent** (dev build is fine); the copy is also guarded by `AdvancedTierInfoTests` (§4.1).
- **Action:** In `Noise control`, click **`Enable advanced tier…`**. Read the dialog against the **warning dialog** reference above. Click **Cancel**.
- **Expected:** A warning dialog titled **`Enable the advanced tier`** shows the verbatim two-requirement text — (1) **test-signing mode you enable yourself** with `bcdedit /set testsigning on` + reboot, stated as *PodBridge never runs bcdedit for you*; (2) **trusting a self-signed test certificate** imported into Trusted Root CA + Trusted Publishers — plus the machine-wide security trade-off and that it is **NOT a Microsoft-signed driver**. Clicking **Cancel** changes **nothing** (opt-in: declining is a no-op; no elevation, no install). No string claims Apple-parity sound or a Microsoft-signed / production driver.
- **Maps to:** issue #45; spec Outcome "the UX and docs are honest about driver signing … state both requirements … make no claim of a Microsoft-signed / production-attested driver"; `AdvancedTierInfoTests`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 Clean-room + honest-surface review — [machine]
- **Needs:** repo.
- **Action:** Read `src\PodBridge.Core\Protocol\AapProtocol.cs` (every constant + its citation), and re-read all Phase-6 user-facing strings (the `Noise control` submenu, the enable dialog, the follow-up toasts, `docs/user/advanced-tier.md`).
- **Expected:** Each AAP opcode/constant carries a documented-fact citation to `docs/research/aap-anc-protocol.md`; the code is a re-statement of reverse-engineered byte facts, **not** copied GPL source or verbatim protocol-doc prose; only the cleartext AAP handshake/control channel is used (MagicPairing is **not** defeated). No user-facing string claims a Microsoft-signed / production driver or Apple-parity sound.
- **Maps to:** spec Verification "documented-fact citation comment … no GPL source or verbatim prose (review)"; constitution clean-room + honest-surface + no-MagicPairing rules.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

> **The remaining cases (§5.7–§5.14) require the driver to be built, trusted, and actually loaded on a real machine.** They are the **Tier-2 manual smoke test** — CI cannot sign-to-load or exercise Bluetooth. Do them on a disposable test box you are willing to put in test-signing mode. **Each enablement step is an explicit, honestly-warned user/admin action; PodBridge performs none of them silently.**

### 5.7 Build + test-sign the driver — [real-hardware+driver]
- **Needs:** Visual Studio + C++ workload + WDK component (or the WDK NuGet).
- **Action:** From a normal shell: `cd driver\PodBridgeAAP` then `.\build-testsign.ps1`.
- **Expected:** A test-signed package (`PodBridgeAAP.sys` / `.inf` / `.cat`) and the self-signed public cert `PodBridgeTest.cer` (subject `CN=PodBridge Test (AAP Driver)`) are produced under `driver\PodBridgeAAP\x64\Release`. The script **installs nothing and changes no security setting** — it only prints the remaining load steps. It is **NOT** a Microsoft-signed build.
- **Maps to:** issue #42/#45; research #40 (d) dev signing chain; `docs/user/advanced-tier.md` §1.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 Enable test-signing mode — the manual step PodBridge NEVER performs — [real-hardware+driver]
- **Needs:** admin on the test box.
- **Action:** From an **elevated** prompt: `bcdedit /set testsigning on`, then **reboot**.
- **Expected:** Windows accepts the setting; after reboot the desktop shows the **"Test Mode"** watermark (expected). Confirm **PodBridge and its installer never ran this** — it is a machine-wide security relaxation on the project deny-list; the app only *documents/reminds*. This is one of the **two** load requirements; on its own it is not enough (see §5.9).
- **Maps to:** spec Constraint "the app never runs `bcdedit` … enabling test-signing mode is a manual user action"; research #40 (d) requirement 1; `docs/user/advanced-tier.md` §3.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Trust the self-signed cert + install the driver (one elevated opt-in step) — [real-hardware+driver]
- **Needs:** §5.7 done; admin. §5.8 (test-signing) should be on and rebooted so the driver can actually load once installed.
- **Action:** Either from inside PodBridge (tray → `Noise control` → `Enable advanced tier…` → **OK** to the warning → approve the single UAC prompt), **or** by hand: `.\install-advanced-tier.ps1 -Action install` (self-elevates, one UAC). Optionally verify the equivalents by hand: `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine root` + `... trustedpublisher`, then `pnputil /add-driver PodBridgeAAP.inf /install`.
- **Expected:** In **one** elevated step the installer imports `PodBridgeTest.cer` into **both** `LocalMachine\Root` **and** `LocalMachine\TrustedPublisher`, then runs `pnputil /add-driver PodBridgeAAP.inf /install`. It **does not** run `bcdedit`. The app itself stays `asInvoker` — only the launched PowerShell elevates. When started from the tray, the `Enable the advanced tier` toast reminds you to do the `bcdedit` step yourself if not already done. Trusting the cert is the **second** mandatory load requirement — without the Trusted Root import, x64 refuses the driver even with test-signing on.
- **Maps to:** issue #45; spec Outcome "a separate, user-triggered, elevated install step — driver INF via `pnputil` plus importing the self-signed test certificate into Trusted Root CA / Trusted Publishers"; research #40 (c)(d); `docs/user/advanced-tier.md` §2.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.10 Driver loads → "Noise control" becomes enabled — [real-hardware+driver]
- **Needs:** §5.7–§5.9 done (test-signing on + cert trusted + driver installed), rebooted, AirPods Pro 2 connected.
- **Action:** Relaunch PodBridge; confirm AirPods Pro 2 are connected; right-click → open `Noise control`.
- **Expected:** The driver loads (verify with `pnputil /enum-drivers` showing the `PodBridgeAAP.inf` package, and the device present in Device Manager under **Bluetooth**). `DriverAapTransport` finds the device interface at startup (`IsAvailable = true`), so the `Noise control` submenu is now **enabled** — `Off` / `Noise Cancellation` / `Transparency` are selectable, the driver-absent line and `Enable advanced tier…` are **hidden**. If the driver does not load, re-check both requirements from §5.8/§5.9 (that is the documented reality, not a Phase-6 defect).
- **Maps to:** spec Outcome "exposes a user-mode WDF device interface … probes for the driver and reports `Unavailable` when absent"; Human QA gate "the test-signed driver actually loads".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.11 Switch Off / ANC / Transparency with echo-confirm reflecting the real state — [real-hardware+driver]
- **Needs:** §5.10 (submenu enabled, AirPods Pro 2 connected).
- **Action:** From `Noise control`, pick `Noise Cancellation`, then `Transparency`, then `Off`, re-opening the submenu between picks. Listen for the actual noise-control change on the AirPods and cross-check against Apple's own control (e.g. iPhone/Control Center or the stem long-press) if available.
- **Expected:** Each pick **audibly changes** the AirPods' noise-control mode, and the submenu's checked item **reflects the confirmed device state** — the change is applied optimistically then confirmed by the AirPods' echo notification within ~2 s. Changing the mode **on the device itself** (stem long-press) also updates the checked item (the same echo frame arrives unsolicited). No UAC prompt at runtime (interface I/O needs no elevation).
- **Maps to:** issue #44; spec Outcome "noise-control switching … applied optimistically and confirmed by the AirPods echo notification"; Manual smoke-test gate.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.12 Timeout-revert when a set is not confirmed — [real-hardware+driver]
- **Needs:** §5.10. To induce a no-echo case: move the AirPods out of range / power them off mid-set, or request `Adaptive` **without** the Adaptive-unlock having taken (which the device echoes back as a different mode).
- **Action:** With the submenu enabled, trigger a set that the device will not confirm (e.g. drop the connection just as you pick a mode).
- **Expected:** The optimistic check briefly moves, then **reverts** to the previous confirmed mode when no matching echo arrives within the window, and a toast fires — title **`Noise control`**, body **`Couldn't confirm the change with your AirPods — reverted.`** The UI never lies about a state the device did not confirm.
- **Maps to:** issue #44; spec Outcome "reverting the UI on timeout with a transient error"; Manual smoke-test gate "an induced no-echo case reverts the UI with an error".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.13 Adaptive offered only on a supported model (Pro 2) — [real-hardware+driver]
- **Needs:** §5.10 with **AirPods Pro 2** connected; ideally also a non-Pro-2 model (e.g. 1st-gen Pro or Max) to contrast.
- **Action:** With Pro 2 connected, open `Noise control` and confirm `Adaptive` is **enabled** and selectable; select it and confirm via echo. If you have a non-Pro-2 model, connect it and re-open the submenu.
- **Expected:** On **Pro 2** the `Adaptive` item is enabled and switching to it confirms (the startup sequence sends the set-specific-features unlock first, so the device honours Adaptive). On a **non-Pro-2** model, `Adaptive` is **disabled** while `Off` / `Noise Cancellation` / `Transparency` stay available — Adaptive is model-gated per `NoiseControlSupport` (Pro 2 only among Phase-6 targets). The broad model/firmware matrix is Phase 8.
- **Maps to:** issue #44; spec Prior decision "Adaptive is offered only where the connected model reports support"; `NoiseControlSupportTests`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.14 Uninstall reverses both changes; Tier-1 unaffected — [real-hardware+driver]
- **Needs:** the driver installed (§5.9).
- **Action:** `cd driver\PodBridgeAAP` then `.\install-advanced-tier.ps1 -Action uninstall` (elevated, self-elevates). Optionally turn off test-signing: `bcdedit /set testsigning off` (elevated, reboot). Relaunch PodBridge.
- **Expected:** The driver is removed (`pnputil /delete-driver <oemNN.inf> /uninstall`) and the test cert is removed from **both** machine stores. After a relaunch the `Noise control` submenu is back to the driver-absent state (disabled + `Enable advanced tier…`), and **every Tier-1 feature still works** — uninstalling the advanced tier does not affect Tier 1. Turning off test-signing removes the Test-Mode watermark after reboot.
- **Maps to:** issue #45; `install-advanced-tier.ps1` uninstall; `docs/user/advanced-tier.md` "Uninstall"; constitution graceful degradation.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

## 6. Known limitations / deferred (documented — do **not** reject Phase 6 for these)

- **AAP noise-control bytes are firmware-fragile.** The byte format is verified against the reference model **AirPods Pro 2 (USB-C), firmware `7A305`** and centralised + cited in the single `AapProtocol` module. Other models / firmware may differ; broad model + firmware coverage (and the full Adaptive-support matrix + firmware-fragility handling) is **Phase 8**, not this phase. Adaptive is deliberately gated to Pro 2 here.
- **Production (attestation / EV) signing + Microsoft Partner Center are DEFERRED.** Phase 6 ships **test-signed** with a locally-generated self-signed certificate — the **only** dev path. A publicly-distributable, attestation-signed driver that loads on stock Windows with **no** test-signing and **no** cert import needs an **EV code-signing certificate (~$250–560/yr)** + a **Microsoft Partner Center** account (submit the CAB, receive a Microsoft-signed driver). That is a paid, human-provisioned prerequisite and is **out of scope**; cross-signing is a dead end (the April-2026 policy removed its trust). Recorded, not promised — no user-facing string claims a Microsoft-signed / production driver.
- **No CI hardware — all driver-load and Bluetooth behaviour is a manual smoke test.** CI can only **compile** the driver (non-blocking `KMDF driver compile-only (x64)`); it cannot sign-to-load, enable test-signing, trust a cert, or exercise Bluetooth. So §5.7–§5.14 (loading + real AirPods) are only ever verified by a human at the QA gate (constitution Tier-2 gate; `docs/workflow.md`).
- **Loading the test-signed driver requires TWO machine-wide changes, by design.** Test-signing mode (`bcdedit`, the user's own step — never PodBridge's) **and** trusting the self-signed cert in Trusted Root CA + Trusted Publishers. Neither alone loads the driver on x64; both are opt-in and reversible, and Tier 1 works without either. This is the honest signing reality, not a bug.
- **MagicPairing is not defeated.** Only the **cleartext** AAP control channel is used (the 16-byte plaintext handshake + plaintext control frames); no crypto is broken (constitution Don'ts).
- **The advanced tier is invasive by nature.** It installs a kernel driver and lowers the machine's driver-security bar while enabled. It is strictly opt-in, explicitly warned, and never installed or elevated silently — the app stays `asInvoker`.

---

## 7. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #6** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, model + firmware, whether test-signing/cert-trust were in place, and repro steps. Re-run this guide after the fix merges.
- **On full PASS:** the advanced-tier milestone is **accepted** — the spec (`docs/specs/archive/spec-advanced-driver-anc.md`) is archived, the roadmap Phase 6 link is repointed to the archive, and **milestone #6 is closed** (the orchestrator performs the merge + close), which unblocks **Phase 7 (Gesture remap)**. The **[machine]** cases (§4, §5.1–§5.6) are the enforceable acceptance baseline; the **[real-hardware+driver]** cases (§5.7–§5.14) are the deferred human smoke test — record them when a suitable test box + AirPods Pro 2 are available. There are **no parked issues** in this milestone.

---

## 8. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- If you installed the driver only for testing, uninstall it (§5.14) and, if you enabled test-signing, turn it back off (`bcdedit /set testsigning off`, elevated, then reboot) to remove the Test-Mode watermark and restore the normal driver-security bar.
- If you built the driver, the `driver\PodBridgeAAP\x64\Release` output (incl. `PodBridgeTest.cer`) can be deleted; it holds only the public cert + the test-signed package.
