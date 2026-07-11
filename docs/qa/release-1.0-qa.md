# PodBridge — Release 1.0 Manual Test Guide (self-contained `.exe` + security hardening)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine. **Release 1.0** (Phase 9, milestone #9) ships the finished driver-free Tier-1 feature set (Phases 1–8) as a **self-contained, single-file `.exe`** downloaded from GitHub Releases and **run directly — no MSIX, no Microsoft Store, no installer, no admin** — behind a **security posture built for downloadable software** (build-provenance attestation + `checksums.sha256` + SBOM, CodeQL, Dependabot, dependency review, hardened + SHA-pinned workflows, `SECURITY.md` + Private Vulnerability Reporting, and a written threat model). It adds **no new device feature**. Each case is tagged **[machine]** (no AirPods, no download needed — repo/CI/unit checks + the driver-absent path) or **[real-hardware]** (needs a human on a **clean Windows 11 machine with real AirPods**, downloading and running the released exe) so the no-hardware cases can be batched first. This manual is the human-QA gate script for **issue #127**; passing it is what lets a maintainer accept Release 1.0.

## 1. Title & Scope

This guide verifies **Release 1.0 — self-contained `.exe` + security hardening** (milestone #9, issue **#127**), the closeable release milestone that **supersedes the unshipped MSIX/Store mechanism of Phase 5** (blocked on a paid Microsoft Partner Center account). Implemented issues (all merged under milestone #9): **#115** self-contained single-file publish config (scoped MSBuild props), **#116** embed `LICENSE`/`NOTICE`/`THIRD-PARTY-NOTICES.md` as assembly resources, **#117** portable `HKCU\…\Run` auto-start adapter, **#122** tag-triggered release workflow (per-RID exe + `checksums.sha256` + SBOM + build-provenance attestation + honest notes), **#118**/**#119** reproducible-build flags + `SECURITY.md`/PVR + `THREAT-MODEL.md`, **#120**/**#121** BLE-input fuzz hardening + no-`BinaryFormatter` confirmation, **#123** hardened/SHA-pinned workflows + CodeQL/Dependabot/dependency review, **#124** Phase-5 MSIX/Store disposition (superseded; #38 closed), **#125** user-docs pivot to download-and-run. Release 1.0 delivers:

- A **self-contained, single-file `.exe`** published per RID (**`win-x64`** and **`win-arm64`**) via `PublishSingleFile` + `SelfContained` + `IncludeNativeLibrariesForSelfExtract` + `PublishReadyToRun`, scoped behind `Condition="'$(RuntimeIdentifier)' != ''"` so the plain `dotnet build`/**Verify path stays green and unchanged**. The exe **runs `asInvoker`, needs no admin, no install, and no sidecar files** — the three legal files (`LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`) are **embedded as assembly resources** (`PodBridge.App.LICENSE` / `PodBridge.App.NOTICE` / `PodBridge.App.THIRD-PARTY-NOTICES.md`), read by `AboutViewModel` via `Assembly.GetManifestResourceStream` (with the hard-coded `FallbackNotices`), so the About window renders the disclaimer + Apache-2.0 line + third-party notices + version **with no file beside the exe** (Apache-2.0 §4).
- A **tag-triggered release workflow** (`.github/workflows/release.yml`, `Release`) on `windows-latest` that, on a `v*` tag push, publishes the per-RID single-file exe(s) + matching `.pdb`, a `checksums.sha256`, a **CycloneDX SBOM**, and a **build-provenance attestation** (`actions/attest-build-provenance`, verifiable with `gh attestation verify`), and creates a **GitHub Release** with honest notes (driver-free Tier-1 scope, measured exe size, expected first-run SmartScreen behaviour, the "Verify your download" steps). A `workflow_dispatch` run exercises the same publish/SBOM/attestation pipeline **without** cutting a Release (dry-run).
- The **opt-in auto-start-at-login** toggle for the portable exe via a **per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`** entry (`RunKeyStartupToggle` behind the Core `IStartupToggle`), **default OFF**, no admin. It **rewrites the stored path on enable** and **self-heals a stale path on every launch** while Enabled, and **honours a Task-Manager disable** (`StartupApproved\Run` → `DisabledByUser`) without silently re-enabling.
- The release ships **unsigned-with-provenance** — the attestation + `checksums.sha256` + SBOM are the trust anchor. A **SignPath Foundation** verified-publisher signature is a **deferred follow-on (issue #126)**; it is never self-signed and the release-cut does not block on it.
- A **repo security posture:** CodeQL (C#) scanning, Dependabot (`nuget` + `github-actions`), a **dependency-review + vulnerable-package** gate on PRs, and **every workflow hardened** (least-privilege `permissions:` blocks, third-party actions pinned to full commit SHAs); a root **`SECURITY.md`** (supported-versions + PVR-only reporting), **Private Vulnerability Reporting enabled**, and a **`THREAT-MODEL.md`** covering the untrusted-BLE surface (`ContinuityParser` + fuzz tests, CVE-2023-24871), the download/supply-chain surface, the single-file `%TEMP%\.net` extraction consideration, and the default-OFF auto-start.

Release 1.0 keeps **Tier 1 fully intact and driver-free.** With the driver **absent** (the default) every Phase 1–8 Tier-1 feature still works, the .NET Verify suite passes, and the app runs `asInvoker` — **no crash, no elevation, no install**.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

Out of scope here: **any new device feature** — pairing (Phase 1), battery + play/pause (Phase 2), codec transparency (Phase 3), mic-profile policy (Phase 4), noise-control switching (Phase 6), gesture remap (Phase 7), model/firmware coverage + diagnostics (Phase 8) are all **packaged and shipped as-is**, not changed here; MSIX/Store distribution (superseded, #124/#38); the Tier-2 KMDF driver + its separate installer/signing (Phase 6, never in the exe); the deferred SignPath verified-publisher signature (#126); instant SmartScreen trust (impossible without the Store — see §6).

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH` for the repo/build cases.
- **No administrator rights** for anything in this guide — Release 1.0 is driver-free, the exe manifest is `asInvoker`, and the HKCU Run key + `%LOCALAPPDATA%` storage are per-user and elevation-free. (The optional Tier-2 driver is a separate opt-in per `docs/qa/phase-6-advanced-driver-anc.md` and is **not** exercised here.)
- Run all repo commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/CI/release checks (§4.2, §5.1–§5.3, §5.12): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the [real-hardware] download-and-run cases (§5.4–§5.11):** a **clean Windows 11 21H2+ machine** (ideally not a dev box, to see the real first-run SmartScreen behaviour), the released assets for the target release (from the GitHub Release once the `v1.0.0` tag is cut — see §5.1), AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max) paired to that PC, and a working Bluetooth radio.
- **For the arm64 case (§5.11):** **Windows-on-ARM hardware** (or an arm64 Windows 11 VM). The arm64 exe cross-compiles from the x64 runner, but its behavioural QA needs arm64 hardware (spec Human-prerequisite). If none is available, record §5.11 as **deferred** — see §6.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run (dev build, driver absent)

The dotnet **Verify** gate builds and tests the **managed** app the normal way (no RID → the single-file publish props are inert). It does **not** publish the single-file exe — that happens only in the release workflow (§5.1). Run each from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path (dev build):**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) `asInvoker`: launch must raise **no UAC prompt** (dev build or published exe). (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) The **dev build is a framework-dependent, multi-file build** with the loose `LICENSE`/`NOTICE`/`THIRD-PARTY-NOTICES.md` copied beside it — the *sidecar-free / embedded-notices* behaviour (§5.7) is a property of the **published single-file exe**, not the dev build, so the "run from an otherwise-empty folder" check must use a released asset. On a dev build the version reads the assembly version, not the release tag.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from the repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` across both test projects — **`PodBridge.Core.Tests` erfolgreich: 301, gesamt: 301** and **`PodBridge.Windows.Tests` erfolgreich: 46, gesamt: 46** (**347 passed, 0 failed, 0 skipped**) — and the **plain build is unchanged** (the scoped publish props do not affect the no-RID path). This includes the device-independent Release-1.0 gates, all driven by fakes with **no driver, no hardware, and no network**:

- **`RunKeyStartupToggleTests`** (Windows, via `FakeRunKeyRegistry` + a fixed process path) — the portable HKCU auto-start adapter: default **OFF** (no Run value → `Disabled`); enable → the Run value is written as the **quoted** current process path; disable → the Run value is cleared; a **stale** stored path is **self-healed** on read while Enabled; a Task-Manager `StartupApproved\Run` disable is reported as **`DisabledByUser`** and never overridden; a registry exception degrades to `Disabled` (never throws).
- **`StartupToggleTests`** (Core, via `FakeStartupToggle`) — the device-independent enable/disable/default-OFF contract behind `IStartupToggle`.
- **`ProductInfoTests`** (Core) — the **branding/disclaimer invariant** still holds with notices embedded: `Name == "PodBridge"` and contains neither "Apple" nor "AirPods"; the not-affiliated disclaimer is present; declared license is `Apache-2.0`; the descriptor uses "for AirPods"; the audio note never claims Apple-parity.
- **`ContinuityParserFuzzTests`** (Core) — the untrusted-BLE hardening gate: truncated / over-long / random Continuity payloads and explicit malformed length/count fields (boundary `0x00`/`0x19`/`0xFF`, over-long values) **never throw**, never read out of bounds, and never mis-identify a known model's fixture; a decoded battery value is always `null` or in `0..100`.
- The full Phase 1–8 Core/Windows suites (registry, capability, diagnostics, logging, mic-policy restore, BLE supervisor, etc.) continue to pass unchanged — Release 1.0 packages them, it does not modify them.

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail. Verify does **not** publish the single-file exe or build the driver — those are the release workflow (§5.1) and the separate Driver CI (Phase 6).

### 4.2 Static / repo inspections — [machine]

The dotnet build does not exercise the single-file publish config, the embedded-resource wiring, the release/security workflows, or the security docs — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **SINGLE-FILE-PUBLISH-PROPS** | `type src\PodBridge.App\PodBridge.App.csproj` | A `PropertyGroup Condition="'$(RuntimeIdentifier)' != ''"` sets `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `PublishReadyToRun` **true**; `RuntimeIdentifiers` is `win-x64;win-arm64`; **`PublishTrimmed` is never set** (WPF disables trimming, NETSDK1168); `EnableCompressionInSingleFile` is left unset (measured, not assumed). |
| **NOTICES-EMBEDDED** | `findstr /C:"EmbeddedResource" /C:"PodBridge.App.LICENSE" /C:"PodBridge.App.NOTICE" /C:"PodBridge.App.THIRD-PARTY-NOTICES.md" src\PodBridge.App\PodBridge.App.csproj` | The three legal files are `<EmbeddedResource>` items with the `PodBridge.App.*` logical names — embedded in the assembly, not `<Content>` sidecars. |
| **NOTICES-READ-FROM-ASSEMBLY** | `findstr /N /C:"GetManifestResourceStream" /C:"FallbackNotices" src\PodBridge.App\AboutViewModel.cs` | `AboutViewModel` reads the notices via `Assembly.GetManifestResourceStream("PodBridge.App.THIRD-PARTY-NOTICES.md")` and keeps a hard-coded `FallbackNotices` — so the single exe is sidecar-free. |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged — no elevation). |
| **PORTABLE-AUTOSTART-ADAPTER** | `dir src\PodBridge.Windows\RunKeyStartupToggle.cs src\PodBridge.Windows\Interop\RunKeyRegistryInterop.cs` and `findstr /I /C:"HKCU" /C:"CurrentVersion\\Run" /C:"DisabledByUser" src\PodBridge.Windows\RunKeyStartupToggle.cs` | The portable HKCU `Run`-key adapter behind `IStartupToggle` exists; it self-heals a stale path and reports `DisabledByUser`. The dead MSIX `StartupTaskToggle` is gone. |
| **REPRODUCIBLE-BUILD-FLAGS** | `type Directory.Build.props` | `PublishRepositoryUrl` + `EmbedUntrackedSources` true, `DotNet.ReproducibleBuilds` referenced; `ContinuousIntegrationBuild` is **not** set here (passed only on the CI publish command — never bakes CI paths into local PDBs). |
| **RELEASE-WORKFLOW** | `type .github\workflows\release.yml` | Workflow `Release` on `push: tags: ['v*']` + `workflow_dispatch`; a **matrix** over `win-x64`/`win-arm64` publishes the single-file exe + `.pdb`; generates `checksums.sha256`, a CycloneDX SBOM, and an `actions/attest-build-provenance` attestation; the `Create GitHub Release` step is gated on `refs/tags/v`; third-party actions pinned to full commit SHAs; least-privilege `permissions:` (`contents: write`, `id-token: write`, `attestations: write`). |
| **WORKFLOWS-HARDENED** | `findstr /I /C:"permissions:" .github\workflows\ci.yml .github\workflows\codeql.yml .github\workflows\dependency-review.yml .github\workflows\release.yml` and `findstr /R /C:"uses:.*@[0-9a-f]" .github\workflows\*.yml` | **Every** workflow has an explicit `permissions:` block; every third-party `uses:` is pinned to a 40-char commit SHA (a `# vX.Y.Z` comment follows), not a floating tag. |
| **CODEQL-PRESENT** | `type .github\workflows\codeql.yml` | CodeQL C# scanning on push-to-main + PR; builds `PodBridge.slnx` explicitly (autobuild misses the `.slnx`). |
| **DEPENDABOT-PRESENT** | `type .github\dependabot.yml` | Dependabot v2 with `nuget` **and** `github-actions` ecosystems, weekly. |
| **DEPENDENCY-REVIEW-PRESENT** | `type .github\workflows\dependency-review.yml` | A `dependency-review` job (PR gate, comment-summary) **and** a `vulnerable-packages` job running `dotnet list package --vulnerable --include-transitive`, failing on any hit. |
| **SECURITY-MD** | `type SECURITY.md` | Supported-versions table (latest release only) + **PVR as the sole report channel** (`security/advisories/new`); no public-issue instruction. |
| **THREAT-MODEL-MD** | `findstr /I /C:"CVE-2023-24871" /C:"attest-build-provenance" /C:"%TEMP%" /C:"BinaryFormatter" /C:"Auto-start" THREAT-MODEL.md` | `THREAT-MODEL.md` covers Surface A (untrusted BLE, CVE-2023-24871, `ContinuityParser` + fuzz), Surface B (download/supply-chain: attestation/checksums/SBOM), the single-file `%TEMP%\.net` extraction, no-`BinaryFormatter`, and the default-OFF auto-start. |
| **NO-BINARYFORMATTER** | `findstr /S /I /C:"BinaryFormatter" src tests` | **No match** in `src`/`tests` — every local persistence path is plain text / `System.Text.Json` (threat-model confirmation). |
| **MSIX-SUPERSEDED** | `dir packaging 2>&1` and `gh issue view 38 --repo bhemsen/PodBridge` | `packaging\` (and `packaging.yml`) are removed; issue **#38** is **closed as superseded** (not completed). |
| **ROADMAP-ARCH-SHIPPED** | `findstr /I /C:"Release 1.0" /C:"self-contained" docs\roadmap.md docs\architecture.md` | `docs/roadmap.md` shows Phase 9 = Release 1.0 (superseding Phase 5's MSIX/Store); `docs/architecture.md` reflects the single-file exe + portable auto-start and drops the MSIX packaging component. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 6` | The latest `CI` (Verify), `CodeQL`, and `Dependency Review` runs on `main` are `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** strings shown.

**Reference — exact strings & asset names.**

- **Release asset names** (`<version>` = the tag without the leading `v`, e.g. `1.0.0`):
  - `PodBridge-<version>-win-x64.exe` + `PodBridge-<version>-win-x64.pdb`
  - `PodBridge-<version>-win-arm64.exe` + `PodBridge-<version>-win-arm64.pdb`
  - `checksums.sha256` (one `<sha256> *<exe-name>` line per exe, lowercase hash)
  - a CycloneDX SBOM file (e.g. `bom.xml` / `bom.json`)
  - `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md` (Apache-2.0 §4 copies alongside the binary)
- **Verify-your-download commands** (exact):
  - Checksum: `certutil -hashfile PodBridge-<version>-win-x64.exe SHA256`
  - Attestation: `gh attestation verify PodBridge-<version>-win-x64.exe -R bhemsen/PodBridge`
- **Expected first-run SmartScreen dialog** (unsigned exe): title **`Windows protected your PC`**, body naming an **`Unknown publisher`**. Proceed only after verifying: **More info → Run anyway**. **Never** disable SmartScreen or instruct a blanket bypass.
- **Tray context menu** (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · `Microphone mode` (submenu) · `Noise control` (submenu) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · `Gesture controls…` · `Export diagnostics` · `Debug logging` · **`About PodBridge`** · `Exit`.
- **About window** — title `About PodBridge`; product name **`PodBridge`** (no Apple logo), descriptor **`for AirPods on Windows`**, **`Version <x.y.z>`** (the release tag on a published exe), the not-affiliated disclaimer, the honest audio/mic note, **`Licensed under the Apache License, Version 2.0 (Apache-2.0).`**, and a scrollable **`Third-party notices`** box (H.NotifyIcon (MIT), Microsoft.Extensions.* (MIT), CsWinRT / Windows SDK projections (MIT), PodBridge itself Apache-2.0).
- **Auto-start checkbox** (in the About window): **`Start PodBridge automatically when I sign in`** (unchecked by default). Windows-blocked hint (shown only on a Task-Manager disable): **`Auto-start is turned off in Windows Settings or Task Manager; change it there.`**
- **Auto-start Run key:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `PodBridge`, data = the quoted current exe path.
- **Per-user data:** `%LOCALAPPDATA%\PodBridge\` — `mic-policy-mode.txt`, `gesture-config.txt`, first-run markers, `diagnostics\*.txt`, `logs\podbridge.log`.

---

### 5.1 Release workflow produces the per-RID assets (+ how to cut the release) — [machine]
- **Needs:** repo + `gh`. Cutting the real `v1.0.0` tag is a maintainer gate action (issue #127) — do it deliberately, not casually.
- **Action:** First inspect the pipeline without triggering it (read `.github/workflows/release.yml`). To produce assets for QA you have two paths: **(a) dry-run** — `gh workflow run Release --repo bhemsen/PodBridge -f version=1.0.0` (runs publish/SBOM/attestation, uploads the `podbridge-exe-*` workflow artifacts, produces the attestation, **but cuts no Release**); **(b) real release** — push the tag: `git tag v1.0.0 && git push origin v1.0.0`. Then `gh run list --repo bhemsen/PodBridge --workflow Release --limit 1`, and for the real release `gh release view v1.0.0 --repo bhemsen/PodBridge`.
- **Expected:** The `Release` run is `success` with a `win-x64` **and** a `win-arm64` publish leg, each producing `PodBridge-<version>-<rid>.exe` + `.pdb`. On a **`v*` tag** it additionally creates a GitHub Release titled `PodBridge v<version>` with all exe(s) + `.pdb`(s) + `checksums.sha256` + the SBOM + `LICENSE`/`NOTICE`/`THIRD-PARTY-NOTICES.md`, and honest notes stating the driver-free Tier-1 scope, the **measured exe size**, the expected SmartScreen behaviour, and the verify steps. A malformed version/tag fails the `version` job loudly. On a `workflow_dispatch` dry-run the assets are only workflow artifacts (no Release) — still enough to run §5.2/§5.3.
- **Maps to:** issue #122; spec Verification "CI on `windows-latest` publishes the self-contained single-file exe for each selected RID"; issue #127 acceptance (tag + Release exist).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 `checksums.sha256` matches each exe; SBOM is attached — [real-hardware or machine]
- **Needs:** the released assets (or dry-run artifacts) from §5.1 in one folder.
- **Action:** For each exe: `certutil -hashfile PodBridge-<version>-win-x64.exe SHA256` and compare (case/space-insensitive) against the matching line in `checksums.sha256`. Confirm the SBOM file is present and lists PodBridge's dependencies.
- **Expected:** Each printed SHA-256 matches its `checksums.sha256` line **exactly**. The CycloneDX SBOM is attached and enumerates the NuGet packages the build was compiled against (H.NotifyIcon, Microsoft.Extensions.*, CsWinRT, DotNet.ReproducibleBuilds, …).
- **Maps to:** issue #122; spec Verification "`checksums.sha256` matches each asset … an SBOM is attached to the release".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 Build-provenance attestation succeeds on the genuine exe and FAILS on a tampered copy — [real-hardware]
- **Needs:** a released/dry-run exe, the `gh` CLI authenticated against `bhemsen/PodBridge`.
- **Action:** (a) On the genuine exe: `gh attestation verify PodBridge-<version>-win-x64.exe -R bhemsen/PodBridge`. (b) Make a tampered copy and re-verify it: `copy PodBridge-<version>-win-x64.exe tampered.exe`, append a byte (`cmd /c "echo x >> tampered.exe"`), then `gh attestation verify tampered.exe -R bhemsen/PodBridge`. Delete `tampered.exe` afterwards.
- **Expected:** (a) The genuine exe **verifies successfully** — the attestation ties it to the `bhemsen/PodBridge` repo/workflow/commit that built it. (b) The tampered copy **fails closed** (no matching attestation for that digest) — proving the check is fail-closed, strictly stronger than a same-page checksum.
- **Maps to:** issue #122; spec Verification "`gh attestation verify <exe> -R bhemsen/PodBridge` succeeds for the released exe, and fails for a tampered copy".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Download & run from an otherwise-empty folder — no admin, no install — [real-hardware]
- **Needs:** a clean Windows 11 machine, the released `PodBridge-<version>-win-x64.exe`.
- **Action:** Create an **empty** folder (e.g. `C:\Users\<you>\PodBridgeTest\`) and place **only** the exe in it (no other file next to it). From a **normal, non-elevated** session, double-click the exe (handle SmartScreen per §5.5).
- **Expected:** PodBridge launches to a **tray icon with no window and no UAC prompt** — running the file **is** the install; there is no setup wizard, no reboot, no elevation. No sidecar files are required next to the exe for it to run. In Task Manager → Details, `PodBridge.App.exe` runs at **Medium** integrity (a normal user process). The single-file runtime self-extracts native libs under the **per-user** `%TEMP%\.net\…` (per the threat model) — not a machine-wide location.
- **Maps to:** spec Outcome "runs `asInvoker`, needs no admin, no install, and no sidecar files"; issue #127 Human-QA "download + run with no admin and no install".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 First-run SmartScreen expectation (verify-then-run, never a blanket bypass) — [real-hardware]
- **Needs:** the clean machine from §5.4 (first run of a freshly-downloaded, unsigned exe).
- **Action:** On the very first launch, observe the SmartScreen dialog. Before proceeding, verify the download per §5.2/§5.3. Then click **More info → Run anyway**.
- **Expected:** Windows SmartScreen shows **`Windows protected your PC`** naming an **`Unknown publisher`** — this is the **expected, honest** behaviour for a new unsigned download, matching the release notes and `docs/user/README.md`. After verifying, **More info → Run anyway** lets it start. PodBridge **never** tells you to disable SmartScreen or blindly bypass it; on an org-managed PC (Smart App Control / enterprise policy) "Run anyway" may be blocked entirely — that is documented and outside PodBridge's control.
- **Maps to:** spec Constraints "never instruct a blanket SmartScreen bypass"; Risks (first-run SmartScreen); issue #127 Human-QA "the expected SmartScreen warning shows".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 ≤ 2-minute fresh-run-to-battery-visible — [real-hardware]
- **Needs:** the running exe (§5.4), AirPods, a working Bluetooth radio. Time it.
- **Action:** Starting a stopwatch at first launch of the freshly-downloaded exe: launch PodBridge; if the AirPods are not paired, right-click the tray → `Pair / Reconnect` (opens Windows Bluetooth settings) and add them; once connected, right-click the tray and read the `Status:` / `Battery:` lines.
- **Expected:** Within **≤ 2 minutes** you reach **AirPods paired, audio playing, battery visible**: `Status:` reads `Connected` and `Battery:` shows left/right/case (e.g. `L 80% · R 75% · Case 90%⚡`). No UAC prompt at any point.
- **Maps to:** spec Verification "tray reaches battery-visible in ≤ 2 minutes"; vision success criterion.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.7 About window shows embedded disclaimer + Apache-2.0 + third-party notices + version — with NO file beside the exe — [real-hardware]
- **Needs:** the published single-file exe run from the **empty** folder (§5.4) — this is the load-bearing proof of embedded resources; a dev build (loose files beside it) does **not** prove it.
- **Action:** With **only** the exe in the folder, launch it, right-click the tray → **`About PodBridge`**. Read every line. Confirm the `Version` matches the release tag.
- **Expected:** The About window opens and shows, from the **embedded** resources (no sidecar file present): the name **`PodBridge`** (no Apple logo), the **`for AirPods on Windows`** descriptor, the **`Version <x.y.z>`** line (= the release version), the boxed **not-affiliated disclaimer**, the **honest audio/mic note** (never claims Apple-parity), the **`Licensed under the Apache License, Version 2.0 (Apache-2.0).`** line, and a populated **`Third-party notices`** box (the embedded `THIRD-PARTY-NOTICES.md`, not the short `FallbackNotices` stub) — all with **no `LICENSE`/`NOTICE`/`THIRD-PARTY-NOTICES.md` file next to the exe**, proving Apache-2.0 §4 compliance for the standalone binary.
- **Maps to:** issue #116; spec Outcome "the About window still renders the disclaimer, Apache-2.0 notice, third-party notices, version … from the embedded copy"; issue #127 Human-QA "About shows the disclaimer + embedded license/notices + version when run from an otherwise-empty folder".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 Auto-start toggle — default OFF, persists across reboot, self-heals after a folder move, honours a Task-Manager disable — [real-hardware]
- **Needs:** the published exe (§5.4).
- **Action:**
  1. Open **`About PodBridge`**; note the **`Start PodBridge automatically when I sign in`** checkbox state on first open.
  2. **Check** it. Confirm `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` now has a `PodBridge` value = the quoted current exe path (`reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PodBridge`), and that **Task Manager → Startup apps** lists PodBridge = Enabled.
  3. **Reboot**; confirm PodBridge auto-starts to the tray on sign-in.
  4. **Exit PodBridge, move the whole exe folder** to a new path (e.g. rename `PodBridgeTest` → `PodBridgeMoved`), and **relaunch** the exe from the new path. Re-open About / re-query the Run key.
  5. **Disable** PodBridge from **Task Manager → Startup apps**, then re-open About.
- **Expected:**
  1. First open = **unchecked** (default OFF — `RunKeyStartupToggle` reports `Disabled` with no Run value; also guarded by `RunKeyStartupToggleTests`/`StartupToggleTests`).
  2. Checking writes the quoted current-path Run value; Startup apps shows **Enabled**.
  3. After reboot PodBridge **auto-starts to the tray** — the setting **persists across reboot**.
  4. After the move-and-relaunch, the Run value is **silently rewritten to the new path** (self-heal on launch while Enabled) — it still points at the real exe.
  5. A Task-Manager disable is **honoured**: the checkbox shows unchecked **and disabled** with the hint `Auto-start is turned off in Windows Settings or Task Manager; change it there.` — PodBridge does **not** silently re-enable it.
- **Maps to:** issue #117; spec Outcome "per-user `HKCU\…\Run` … default OFF … rewrites the stored path on enable and self-heals a stale path on launch … honours a user's Task-Manager disable"; issue #127 Human-QA (auto-start persists + self-heals after move).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Settings & logs live under `%LOCALAPPDATA%\PodBridge` — [real-hardware]
- **Needs:** the running exe (§5.4), ideally after choosing a mic mode and exporting diagnostics once.
- **Action:** `explorer "%LOCALAPPDATA%\PodBridge"`. Confirm the folder is created and populated as you use the app (pick a `Microphone mode`, then tray → `Export diagnostics`, then toggle `Debug logging`).
- **Expected:** `%LOCALAPPDATA%\PodBridge\` contains the per-user data: `mic-policy-mode.txt` (chosen mode), first-run markers, `diagnostics\podbridge-diagnostics-*.txt` (address-masked, secret-free), and `logs\podbridge.log` (self-capped ~10 MB / 7 days). Nothing is written to a machine-wide or admin location; no registry install entry beyond the optional per-user auto-start Run key.
- **Maps to:** spec Outcome/Verification "where settings/logs live (`%LOCALAPPDATA%`)"; issue #127 Human-QA "settings/logs appear under `%LOCALAPPDATA%\PodBridge`".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.10 Tier-1 works with NO driver present; app runs `asInvoker` — [real-hardware]
- **Needs:** the running exe (§5.4) on a machine with **no PodBridge driver installed** (the clean-machine default), AirPods connected.
- **Action:** With no driver present, exercise the Tier-1 features: battery/status, `Refresh audio status` (codec + mic lines), the `Microphone mode` submenu, `Export diagnostics`. Confirm no elevation prompt occurred at launch or during use. Open the `Noise control` submenu.
- **Expected:** Every **Tier-1** feature works driver-free (battery, play/pause, codec transparency, mic-policy) and the app ran with **no UAC prompt** (Medium integrity). The **Tier-2** `Noise control` submenu is honestly disabled with an `Enable advanced tier…` entry (never falsely claimed). The exported diagnostics show `Driver present: False` / `Tier 1 (driver-free)`. This mirrors the device-independent driver-absent suite that passes in §4.1.
- **Maps to:** spec Outcome "the Tier-1 suite passes with no driver present … the app runs `asInvoker` (no elevation prompt)"; constitution graceful degradation.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.11 arm64 asset runs natively on Windows-on-ARM — [real-hardware]
- **Needs:** **Windows-on-ARM hardware** (or an arm64 Windows 11 VM) and the `PodBridge-<version>-win-arm64.exe`. If unavailable, record as **deferred** (see §6) — do not fail Release 1.0 for lack of arm64 hardware.
- **Action:** On the arm64 machine, verify the arm64 exe (§5.2/§5.3), then run §5.4–§5.10 against the **arm64** asset.
- **Expected:** The arm64 exe verifies (its own `checksums.sha256` line + attestation), runs **natively** (not under x64 emulation), and behaves identically to x64: no admin/install, About/embedded-notices, auto-start, `%LOCALAPPDATA%` data, Tier-1 driver-free. Confirm it is arm64 in Task Manager → Details → Architecture (`ARM64`, not `x64`).
- **Maps to:** spec Decision "Target arch = `win-x64` + `win-arm64`"; Human-prerequisite (arm64 hardware at the QA gate); issue #127 Human-QA (both x64 and arm64).

`[ ] PASS   [ ] FAIL / [ ] DEFERRED (no arm64 hardware)`  Notes: ______________________________

### 5.12 Security posture spot-checks (SECURITY.md/PVR, CodeQL/Dependabot/dependency-review, THREAT-MODEL.md) — [machine]
- **Needs:** repo + `gh`; repo-admin visibility for the PVR/scanning toggles.
- **Action:** Confirm the §4.2 file checks (`SECURITY-MD`, `THREAT-MODEL-MD`, `CODEQL-PRESENT`, `DEPENDABOT-PRESENT`, `DEPENDENCY-REVIEW-PRESENT`, `WORKFLOWS-HARDENED`) passed. Then confirm the **admin toggles** are actually on (owner/admin): `gh api repos/bhemsen/PodBridge/private-vulnerability-reporting` reports enabled; **Settings → Security** shows CodeQL code scanning active, Dependabot (version + security) on, and secret scanning + push protection on; and the latest `CodeQL` / `Dependency Review` runs are `success` (`gh run list --repo bhemsen/PodBridge --limit 8`).
- **Expected:** `SECURITY.md` (+ PVR as the sole report channel) and `THREAT-MODEL.md` are present and cover the documented surfaces; CodeQL/Dependabot/dependency-review workflows are present, hardened (least-privilege `permissions:` + SHA-pinned actions), and green; PVR + secret scanning + push protection are enabled. The workflow/config are in-repo (verifiable here); the admin toggles are the maintainer's confirmation.
- **Maps to:** issues #118/#119/#123; spec Verification "CodeQL runs … Dependabot config is active … dependency-review gate … SECURITY.md exists, PVR is enabled, THREAT-MODEL.md covers the documented surfaces".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

## 6. Known limitations / known debt (documented — do **not** reject Release 1.0 for these)

These are honest, recorded limitations surfaced by the spec/research. None blocks acceptance.

1. **First-run SmartScreen has no instant fix without the Store.** An unsigned download always trips SmartScreen "Unknown publisher" on a fresh machine; **no** option except the excluded Microsoft Store removes it instantly. Reputation builds over time as more users run the same (eventually one-signing-identity) build. The honest mitigation is the verify-then-run walkthrough (§5.5) — never a blanket bypass. **Smart App Control / enterprise policy** can block an unsigned exe outright on locked-down machines; attestation + checksums remain the integrity fallback.
2. **SignPath verified-publisher signing is DEFERRED — issue #126.** Release 1.0 ships **unsigned-with-provenance** (attestation + `checksums.sha256` + SBOM are the trust anchor). A free SignPath Foundation OSS signature is pursued separately (SignPath requires the project to be **already released** in the form to be signed, so first signing lags the first tag); a signed re-release follows once approved. The release-cut does **not** block on it, and it is **never self-signed**.
3. **arm64 behavioural QA needs arm64 hardware.** The arm64 exe cross-compiles from the x64 runner and is attested/checksummed, but §5.11 can only be **behaviourally** verified on Windows-on-ARM. If no arm64 hardware/VM is available, record §5.11 as **deferred** — it does not block the x64 acceptance.
4. **Moved-and-never-relaunched auto-start edge.** The HKCU Run value self-heals on the **next launch** while Enabled; if a user moves the exe folder and **never relaunches** it before signing out, the stale Run entry silently won't fire (no OS warning) until the next manual launch heals it. Documented edge, not a bug.
5. **Single-file `%TEMP%\.net` extraction assumes per-user `%TEMP%`.** On a standard Windows 11 install `%TEMP%` is per-user (`%LOCALAPPDATA%\Temp`), so the native-lib self-extract is not shared across principals. A misconfigured machine-wide `%TEMP%` or a `DOTNET_BUNDLE_EXTRACT_BASE_DIR` override to a shared directory is the operator's own deviation (see `THREAT-MODEL.md`).
6. **Honest audio + mic caveats restated (unchanged platform limits, not defects):**
   - **AAC vs SBC:** on supported hardware Windows plays media over **AAC** (the best codec available on Windows — not Apple-identical); on SBC-only hardware/drivers, quality is lower. PodBridge reports the negotiated codec truthfully and offers no "force AAC" button.
   - **A2DP↔HFP mic trade-off:** using the AirPods microphone forces the Bluetooth **HFP** call profile and collapses media to mono call quality — a **Bluetooth-Classic platform limit, not a PodBridge bug**. The mic-profile policy manages *which device* holds the mic role; it does not remove the trade-off.

Additional standing constraints carried forward (unchanged): **MagicPairing is not defeated** (cleartext AAP only); **no CI hardware** — all real-AirPods behaviour is a manual smoke test; the Tier-2 KMDF driver and its production signing remain a separate opt-in (Phase 6), never in the exe.

---

## 7. Recording results & regressions

- Mark each case `PASS` / `FAIL` (or `DEFERRED` for §5.11 without arm64 hardware) above, including §4, and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #9** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string/behaviour, OS build + architecture, the asset version, whether SmartScreen/attestation were checked, and repro steps. Re-run this guide after the fix merges.
- **On full PASS of the shipped scope** (§4, §5.1–§5.10, §5.12; §5.11 pass **or** deferred): Release 1.0 is **accepted** — the `v1.0.0` tag + GitHub Release are the deliverable, `docs/roadmap.md`/`docs/architecture.md` reflect the shipped state, and **milestone #9 + issue #127 are closed** by the maintainer. The **[machine]** cases (§4, §5.1, §5.12) are the enforceable baseline; the **[real-hardware]** cases (§5.2–§5.11) are the human smoke test on a clean Windows 11 box.

---

## 8. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- If you enabled auto-start (§5.8), turn it back off from **`About PodBridge`** (or Task Manager → Startup apps) unless you want it to persist; to fully remove: `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v PodBridge /f`.
- Delete the test exe and the `%LOCALAPPDATA%\PodBridge` folder (mic-mode setting, first-run markers, diagnostics, logs) if you were only testing — that is the entire removal (no installer, no Store package).
- Delete any `tampered.exe` created in §5.3.
