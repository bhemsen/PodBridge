# PodBridge — Phase 5 Manual Test Guide (Packaging & distribution)

> Open-source companion **for AirPods on Windows**. Not affiliated with Apple. This guide is executed by a human at a Windows 11 machine. Phase 5 adds **no new device features** — it packages and ships the finished driver-free feature set (Phases 1–4). Each case is tagged **[machine]** (no AirPods, no MSIX install needed — mostly repo/CI/unit checks) or **[real-hardware]** (needs a real install and/or AirPods on a Windows 11 box) so the no-hardware cases can be batched first. One case is tagged **[real-hardware · admin-for-trust-only]**: the *only* admin action in all of Phase 5 is the one-time certificate-trust for the manual-fallback MSIX; nothing else in this phase requests elevation.

## 1. Title & Scope

This guide verifies **Phase 5 — Packaging & distribution** (milestone #5), the **driver-free MVP** that concludes Tier 1. Implemented issues (all merged): **#31** research (MSIX packaging / no-admin signing / `windows.startupTask` / winget-`msstore`), **#32** Apache-2.0 licensing (`LICENSE` + `NOTICE` + `THIRD-PARTY-NOTICES.md` + SPDX metadata), **#33** About window, **#34** signed MSIX via `.wapproj` + CI, **#35** opt-in auto-start via `windows.startupTask` (default OFF), **#36** release workflow + self-signed GitHub-Releases MSIX + `msstore`/Store-association prep, **#37** user docs (README quickstart + `docs/user/`). Phase 5 wraps Phases 1–4 into a distributable product:

- A **signed, app-only MSIX** built from the Windows Application Packaging Project `packaging/PodBridge.Package/PodBridge.Package.wapproj`, produced on `windows-latest` by the **`Package & Release (MSIX)`** workflow (`.github/workflows/packaging.yml`). Branch/PR builds publish the MSIX + trust certificate as the **`PodBridge-msix`** workflow artifact; a semver tag `vMAJOR.MINOR.PATCH` additionally cuts a **GitHub Release** with `PodBridge-<tag>.msix` + `PodBridge-SelfSigned.cer`. Signing uses an in-workflow **self-signed throwaway** certificate (Subject `CN=PodBridge (Self-Signed CI)`) — no signing secret. The workflow asserts the package is **app-only** (no `.sys` driver payload).
- The app's **first non-tray window — the About window** — reachable from a tray **`About PodBridge`** entry, carrying the coined name (no "Apple"/"AirPods" in the name, no Apple logo), the "for AirPods on Windows" descriptor, the **not-affiliated disclaimer**, the **Apache-2.0** license line + third-party notices, the app version, an **honest audio/mic note** (never claims Apple-parity sound), a docs link, and the auto-start toggle. Its device-independent strings live in `PodBridge.Core.Branding.ProductInfo`.
- **Opt-in auto-start-at-login** via the MSIX `windows.startupTask` extension (`TaskId="PodBridgeStartup"`, `Enabled="false"` — default **OFF**), toggled from the About window, behind the Core `IStartupToggle` interface with the `PodBridge.Windows` `StartupTaskToggle` (over the WinRT `StartupTask` API) adapter.
- **Two distribution channels:** the **Microsoft Store** via winget `msstore` is the primary **no-admin** path (Store trust is pre-installed); the **self-signed GitHub-Releases MSIX** is a documented **manual fallback** needing a one-time, admin-only certificate trust. The `msstore` entry + Store-association manifest are **prepared** here against a Partner-Center-derived placeholder ID; the Store submission itself is **parked** (issue **#38**).
- **User docs** (root `README.md` quickstart + `docs/user/README.md`) covering install (Store/winget + manual MSIX), the ≤ 2-minute setup, honest audio/mic caveats, the mic-profile modes, the auto-start toggle, uninstall, and the disclaimer.

Phase 5 keeps **Tier 1: driver-free and no administrator rights at run time.** The packaged app runs `asInvoker` / `Windows.FullTrustApplication` (full trust = normal desktop app, **not** elevation) and must never raise a UAC prompt. The single admin action anywhere in Phase 5 is the one-time cert-trust for the *manual* MSIX; the Store channel needs none.

> **Honest packaging reality (read this first).** A sideloaded MSIX signed with a **self-signed** certificate is **not trusted by Windows by default**, so it will not install until its certificate is imported into the **machine** `TrustedPeople` store — a one-time step that **requires admin**. That is exactly why the self-signed GitHub-Releases MSIX is a *fallback*, not the no-admin path. The genuine **no-admin** install is the **Microsoft Store** channel (Microsoft re-signs for free; Store trust ships with Windows) surfaced via `winget install <StoreProductId> -s msstore --scope user`. **That channel only becomes live after the Store submission is certified (issue #38), which is PARKED** — it needs a Microsoft Partner Center account and a real-hardware no-admin QA pass that cannot be done here. Until then the self-signed manual channel is the only way to install a build. See §6 for the full parked list.

> **N/A for this milestone:** SEO / Lighthouse / ARIA / colour-contrast / accessibility-tree checks are **not applicable** — PodBridge is a Windows desktop **tray** app, not a web page.

Out of scope here: any new device feature (pairing = Phase 1, battery + play/pause = Phase 2, codec transparency = Phase 3, mic-profile policy = Phase 4 — all *packaged* here, not built or changed); the KMDF L2CAP driver + its separate INF/`pnputil` installer + ANC/Transparency/Adaptive switching (Phase 6); gesture remap (Phase 7); broader model support + diagnostics (Phase 8).

---

## 2. Prerequisites

- **Windows 11 21H2 or newer** (OS build **22621+**), **.NET 10 SDK** (`10.0.x`) on `PATH`.
- **No administrator rights** for anything except the one documented cert-trust step (§5.3) — Tier 1 is driver-free and the manifest is `asInvoker`.
- Run all repo commands from the repo root: `C:\Users\bhemsen\Documents\Privat\bluetooth_connector`.
- For the repo/CI checks (§4.2, §5.1–5.2): the [`gh`](https://cli.github.com) CLI authenticated against `bhemsen/PodBridge`.
- **For the MSIX install/launch/reboot cases (§5.3–5.9):** a Windows 11 21H2+ machine, the signed MSIX + matching `.cer` (from the CI `PodBridge-msix` artifact or a GitHub Release), and — for the ≤ 2-minute battery-visible and auto-start-persistence cases — AirPods paired to that PC and a working Bluetooth radio. *Not needed for the machine cases* — those are repo/CI/unit-test checks.

> **Localization note:** build/test output on this machine is German (`Der Buildvorgang wurde erfolgreich ausgeführt.`, `Bestanden!`). An English SDK prints `Build succeeded` / `Passed!` — identical meaning.

---

## 3. Build & Run (dev build, no packaging)

The dotnet **Verify** gate builds and tests the app the normal way; it does **not** build the MSIX (the `.wapproj` needs full MSBuild and is intentionally kept out of `PodBridge.slnx`). Run each from the repo root, in order.

| # | Command | Expected result |
|---|---------|-----------------|
| 1 | `dotnet restore PodBridge.slnx` | up-to-date / restored, no errors. |
| 2 | `dotnet build PodBridge.slnx -c Release` | `Der Buildvorgang wurde erfolgreich ausgeführt.` — **0 warnings / 0 errors**. |
| 3 | `start "" "src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe"` | No window/console; a PodBridge tray icon appears, **no UAC prompt**. |

**Absolute exe path:**
`C:\Users\bhemsen\Documents\Privat\bluetooth_connector\src\PodBridge.App\bin\Release\net10.0-windows10.0.22621.0\PodBridge.App.exe`

**Stop cleanly:** right-click the tray icon → **Exit**. Fallback: `taskkill /IM PodBridge.App.exe`.

> **Gotchas:** (a) An **unpackaged** dev run has no MSIX package identity, so the About auto-start checkbox falls back to **OFF/Disabled** (WinRT `StartupTask` needs package identity — graceful by design) and the About version reads the assembly version, not the MSIX `<Identity Version>`. To exercise the real auto-start behaviour you must install the **MSIX** (§5.7). (b) Single-instance guard (Phase 1): a second launch shows `PodBridge is already running.` and exits. (c) `asInvoker`: neither the dev exe nor the installed MSIX may raise a UAC prompt.

---

## 4. Automated checks (machine-verified baseline — do these first)

All commands run from the repo root.

### 4.1 Verify gate (build + analyzers + format + tests) — [machine]

Run **after** `dotnet restore PodBridge.slnx`:

```
powershell -NoProfile -File build/verify.ps1
```

**Expected:** exit code 0 — build Release (**0 warnings / 0 errors**, warnings-as-errors in Core), `dotnet format --verify-no-changes` clean, and `Bestanden!` / `Passed!` with **erfolgreich: 112, gesamt: 112** (112 passed, 0 failed, 0 skipped). This includes the two **device-independent Phase-5 gates**: `ProductInfoTests` (the branding/disclaimer/license invariant — name contains neither "Apple" nor "AirPods", name is `PodBridge`, the not-affiliated disclaimer is present, declared license is `Apache-2.0`, the descriptor uses "for AirPods", and the audio note never claims Apple-parity) and `StartupToggleTests` (auto-start default **OFF**, enable → registered, disable → cleared, and a user disable is not silently overridden — driven through a `FakeStartupToggle`).

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

> `verify.ps1` runs `dotnet test --no-build` on the Release build it performs first. Running `dotnet test --no-build` alone (without a prior Release build) will fail.

### 4.2 Static / repo inspections — [machine]

The dotnet build does not exercise the MSIX manifest, the packaging workflow, the licensing files, or the research artefact — check each explicitly:

| Item | Check (from repo root) | Expected |
|------|------------------------|----------|
| **LICENSING-FILES-EXIST** | `dir LICENSE NOTICE THIRD-PARTY-NOTICES.md` | All three exist at the repo root (Apache-2.0 `LICENSE`, trademark/attribution `NOTICE`, per-dependency `THIRD-PARTY-NOTICES.md`). |
| **BRANDING-CONSTANT** | `type src\PodBridge.Core\Branding\ProductInfo.cs` | Coined `Name = "PodBridge"`; `Descriptor = "for AirPods on Windows"`; `LicenseId = "Apache-2.0"`; a `Disclaimer` containing "not affiliated"; an `AudioNote` containing "never claims"; no P/Invoke (Core stays OS-free). |
| **ABOUT-TYPES-EXIST** | `dir src\PodBridge.App\AboutWindow.xaml src\PodBridge.App\AboutWindow.xaml.cs src\PodBridge.App\AboutViewModel.cs` | All exist. |
| **STARTUP-TYPES-EXIST** | `dir src\PodBridge.Core\Startup\IStartupToggle.cs src\PodBridge.Core\Startup\StartupToggleState.cs src\PodBridge.Windows\StartupTaskToggle.cs` | All exist; the Core interface + state enum carry no WinRT dependency, the WinRT adapter lives in `PodBridge.Windows`. |
| **PACKAGING-FILES-EXIST** | `dir packaging\PodBridge.Package\PodBridge.Package.wapproj packaging\PodBridge.Package\Package.appxmanifest packaging\README.md packaging\release-notes.template.md packaging\msstore\msstore-entry.yaml packaging\msstore\store-association.template.xml` | All exist. |
| **MANIFEST-IDENTITY** | `type packaging\PodBridge.Package\Package.appxmanifest` | `Identity Name="PodBridgeContributors.PodBridge"`, `Publisher="CN=PodBridge (Self-Signed CI)"` (throwaway self-signed identity — the Store-matching identity is Partner-Center-derived, applied in #38); `DisplayName>PodBridge`; capabilities are exactly `runFullTrust` + `bluetooth` (no elevated/privacy-broad caps). |
| **STARTUPTASK-DEFAULT-OFF** | `findstr /C:"windows.startupTask" /C:"PodBridgeStartup" /C:"Enabled=\"false\"" packaging\PodBridge.Package\Package.appxmanifest` | The `uap5:Extension Category="windows.startupTask"` with `TaskId="PodBridgeStartup"` and **`Enabled="false"`** (the load-bearing default-OFF). `TaskId` matches `StartupTaskToggle.TaskId`. |
| **ASINVOKER-MANIFEST** | `type src\PodBridge.App\app.manifest` | Contains `level="asInvoker"`; **no** `requireAdministrator` / `highestAvailable` (unchanged by Phase 5). |
| **APP-ONLY-NO-DRIVER** | `findstr /I ".sys" packaging\PodBridge.Package\Package.appxmanifest packaging\PodBridge.Package\PodBridge.Package.wapproj` | **No match** — the MSIX is app-only; the CI workflow additionally unpacks the built MSIX and fails if any `.sys` is present (`Assert app-only` step). |
| **PACKAGING-WORKFLOW** | `type .github\workflows\packaging.yml` | Workflow `Package & Release (MSIX)` on `windows-latest`; builds the `.wapproj`, signs with the self-signed `CN=PodBridge (Self-Signed CI)`, uploads the **`PodBridge-msix`** artifact (`.msix` + `.cer`), and on a `v*` tag publishes a GitHub Release with `PodBridge-<tag>.msix` + `PodBridge-SelfSigned.cer`. |
| **MSSTORE-PREP** | `type packaging\msstore\msstore-entry.yaml` | `source: msstore`; `packageIdentifier: STORE_PRODUCT_ID_TBD` (placeholder — the real 12-char ID is Partner-Center-derived, filled in #38); `installCommand: winget install STORE_PRODUCT_ID_TBD -s msstore --scope user`; `verified: false`, `resolvableAfter: store-certification (#38)`. |
| **RESEARCH-ARTEFACT** | `gh issue view 31 --repo bhemsen/PodBridge --comments`, then `dir docs\research\msix-packaging.md` | A `## Research:` comment (Sources / Consensus / Disputes) covering single-project/`.wapproj` MSIX for WPF/.NET 10, signing & no-admin-install semantics, `windows.startupTask`, and the winget/`msstore` manifest; its consensus is reflected in the manifest + workflow. `docs/research/msix-packaging.md` present. |
| **CI-GREEN-MAIN** | `gh run list --repo bhemsen/PodBridge --branch main --limit 3` | The latest `CI` (Verify) run and the `Package & Release (MSIX)` run on `main` are both `success`. |

`[ ] PASS   [ ] FAIL`   Notes: ____________________________________________

---

## 5. Manual test cases

For each: perform the **Action**, compare against **Expected**, tick the box, add notes. Use the **exact** UI strings shown.

**Reference — exact UI strings.**

- **Tray context menu** (top→bottom): `Status: —` · `Battery: —` · `Codec: —` · `Mic: —` (all disabled) · *(separator)* · **`Microphone mode`** (submenu, Phase 4) · `Refresh audio status` · `Pair / Reconnect` · `Open Bluetooth settings` · *(separator)* · **`About PodBridge`** · `Exit`
- **About window** — title bar `About PodBridge`; then, top to bottom:
  - Product name **`PodBridge`** (no Apple logo anywhere in the window).
  - Descriptor **`for AirPods on Windows`**.
  - Version line **`Version <x.y.z>`** (the packaged MSIX `<Identity Version>`; on an unpackaged dev run, the assembly version).
  - Tagline **`An open-source AirPods companion for Windows.`**
  - Disclaimer (boxed): **`PodBridge is not affiliated with, authorized, sponsored, or endorsed by Apple Inc. "AirPods" and "Apple" are trademarks of Apple Inc., used here only descriptively to identify the hardware this software works with. PodBridge uses no Apple logo.`**
  - Audio/mic note: **`Audio honesty: PodBridge never claims Apple-identical sound. On supported hardware Windows plays media over AAC, the best codec available on Windows, but not identical to Apple's. Using the AirPods microphone forces a Bluetooth call profile (HFP) that drops playback to mono call quality; this A2DP-to-HFP trade-off is a Bluetooth-Classic platform limit, not a bug. PodBridge manages it, it does not solve it.`**
  - License line: **`Licensed under the Apache License, Version 2.0 (Apache-2.0).`**
  - Auto-start checkbox: **`Start PodBridge automatically when I sign in`** (unchecked by default). Hint (shown **only** when Windows blocks it): **`Auto-start is turned off in Windows Settings or Task Manager; change it there.`**
  - Links: **`User documentation`** (→ `https://github.com/bhemsen/PodBridge/tree/main/docs`) · **`Project page`** (→ `https://github.com/bhemsen/PodBridge`).
  - Heading **`Third-party notices`** then a scrollable read-only box (from the shipped `THIRD-PARTY-NOTICES.md`, listing H.NotifyIcon (MIT), Microsoft.Extensions.* (MIT), Windows SDK .NET projections / CsWinRT (MIT), PodBridge itself Apache-2.0).
  - **`Close`** button.
- **Install / trust commands** (exact):
  - No-admin Store channel: `winget install <StoreProductId> -s msstore --scope user` *(parked until #38 — see §6)*.
  - Manual-fallback trust (elevated, once): `Import-Certificate -FilePath .\PodBridge-SelfSigned.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`
  - Manual-fallback install (no admin): `Add-AppxPackage -Path .\PodBridge-<tag>.msix`

---

### 5.1 CI produces the signed MSIX + trust certificate artifact — [machine]
- **Needs:** repo + `gh`.
- **Action:** On the latest `Package & Release (MSIX)` run on `main`, confirm the artifact. `gh run list --repo bhemsen/PodBridge --workflow "Package & Release (MSIX)" --branch main --limit 1` → note the run id → `gh run view <id> --repo bhemsen/PodBridge`. Optionally download: `gh run download <id> --repo bhemsen/PodBridge -n PodBridge-msix -D .\_msix`.
- **Expected:** The run is `success` and exposes a **`PodBridge-msix`** artifact containing a signed `.msix` (self-signed Subject `CN=PodBridge (Self-Signed CI)`) **and** `PodBridge-SelfSigned.cer`. The `Assert app-only (no kernel driver in the package)` step passed (no `.sys` payload). No signing secret was required.
- **Maps to:** issue #34/#36; spec Verification "CI on `windows-latest` builds a signed MSIX and publishes it as a workflow artifact".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.2 Release tag publishes a GitHub Release with MSIX + .cer — [machine]
- **Needs:** repo + `gh` (inspection only — do **not** push a tag as part of QA unless cutting a real release).
- **Action:** Inspect the release path without triggering it: read the `Publish GitHub Release (release builds only)` and `Stamp package version from tag` steps in `.github/workflows/packaging.yml`, and `packaging/release-notes.template.md`. If a `v*` tag/release already exists, `gh release list --repo bhemsen/PodBridge` and `gh release view <tag> --repo bhemsen/PodBridge`.
- **Expected:** Pushing a semver tag `vMAJOR.MINOR.PATCH` stamps the MSIX `<Identity Version>` to `MAJOR.MINOR.PATCH.0`, reuses the same build+sign steps, and creates a GitHub Release titled `PodBridge <tag>` with assets **`PodBridge-<tag>.msix`** + **`PodBridge-SelfSigned.cer`** and notes from the template (self-signed manual-fallback wording, the one-time trust step, and the honest Tier-1 scope). A malformed tag fails the build loudly.
- **Maps to:** issue #36; spec Verification "release workflow builds + signs the MSIX and attaches it to a GitHub Release (self-signed manual channel)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.3 One-time certificate trust for the manual MSIX — [real-hardware · admin-for-trust-only]
- **Needs:** a Windows 11 machine + the `PodBridge-<tag>.msix` and `PodBridge-SelfSigned.cer` from a Release (or the CI artifact) in one folder.
- **Action:** From an **elevated (Run as administrator)** PowerShell, in that folder, run exactly: `Import-Certificate -FilePath .\PodBridge-SelfSigned.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople`
- **Expected:** The certificate imports into `LocalMachine\TrustedPeople`. This is the **only** admin step in Phase 5 and is required **because** the signer is self-signed (App Installer checks the machine `TrustedPeople` store). The `.cer` is the public certificate only (no private key). To undo later: remove that certificate from `Cert:\LocalMachine\TrustedPeople` (admin). This step confirms the honest packaging reality — the manual channel is a fallback, not the no-admin path.
- **Maps to:** issue #36; spec Constraints "the self-signed GitHub-Releases MSIX is a documented manual fallback… the certificate must be imported once from an elevated session"; `packaging/README.md` / `docs/user/README.md` §B.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.4 Install the MSIX and launch with NO admin prompt (asInvoker / full-trust) — [real-hardware]
- **Needs:** §5.3 completed (certificate trusted).
- **Action:** From a **normal, non-elevated** PowerShell in the same folder: `Add-AppxPackage -Path .\PodBridge-<tag>.msix`. Then launch **PodBridge** from the Start menu.
- **Expected:** The package installs **with no admin prompt** (only the earlier cert-trust needed admin; the install itself does not). PodBridge launches to a **tray icon with no window and no UAC prompt**. In Task Manager → Details, `PodBridge.App.exe` runs at **Medium** integrity (a normal user process) — `runFullTrust` means "normal desktop app", **not** elevation. No driver/INF/`pnputil` step occurred.
- **Maps to:** spec Outcome "the packaged app still runs `asInvoker` (no elevation) and installs and runs with no admin and no driver"; Verification "the packaged app runs `asInvoker` (no admin prompt)".

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.5 ≤ 2-minute fresh-install-to-battery-visible flow — [real-hardware]
- **Needs:** installed MSIX (§5.4), AirPods, a working Bluetooth radio. Time it.
- **Action:** Starting a stopwatch at first launch of the freshly-installed app: launch PodBridge; if AirPods are not paired, right-click the tray → `Pair / Reconnect` (opens Windows Bluetooth settings) and add them; once connected, right-click the tray and read the `Status:` / `Battery:` lines.
- **Expected:** Within **≤ 2 minutes** you reach **AirPods paired, audio playing, battery visible**: `Status:` reads `Connected` and `Battery:` shows left/right/case (e.g. `L 80% · R 75% · Case 90%⚡`). No UAC prompt at any point. (This mirrors the vision success criterion; follow `docs/user/README.md` "Set up in under 2 minutes".)
- **Maps to:** spec Verification "app starts to the tray in ≤ 2 minutes to battery-visible"; vision success criterion.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.6 About window — opens from tray, shows all required content — [machine (strings unit-tested) + real-hardware (visual)]
- **Needs:** running app (dev build from §3 is sufficient for the visual check; the strings are also guarded by `ProductInfoTests` in §4.1).
- **Action:** Right-click the tray icon → **`About PodBridge`**. Read every line against the **About window** reference strings above. Click **`User documentation`** and **`Project page`**.
- **Expected:** The window titled `About PodBridge` opens and shows, verbatim: the name **`PodBridge`** (no Apple logo), the **`for AirPods on Windows`** descriptor, the **`Version <x.y.z>`** line, the tagline, the boxed **not-affiliated disclaimer**, the **honest audio/mic note** (never claims Apple-parity), the **`Licensed under the Apache License, Version 2.0 (Apache-2.0).`** line, the **`Third-party notices`** box (Apache-2.0 + MIT deps), and the two working links (open the GitHub docs tree / project page in the default browser). No string claims Apple-identical sound or promises to "solve"/"fix" the mic trade-off.
- **Maps to:** issues #32/#33; spec Verification "the About window opens from the tray 'About' entry and shows the coined name, 'for AirPods' descriptor, the not-affiliated disclaimer, Apache-2.0 + third-party notices, version, honest audio/mic note, and a docs link"; branding invariant is additionally covered by `ProductInfoTests`.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.7 Auto-start toggle — default OFF, enable registers, disable clears, survives reboot — [real-hardware]
- **Needs:** the **installed MSIX** (§5.4) — auto-start needs MSIX package identity; an unpackaged dev run always shows it OFF/blocked and cannot register the task.
- **Action:** Open **`About PodBridge`**. (a) Note the **`Start PodBridge automatically when I sign in`** checkbox state on first open. (b) **Check** it, then open **Task Manager → Startup apps** (or **Settings → Apps → Startup**) and find **PodBridge**. (c) **Reboot** and confirm PodBridge launches to the tray on sign-in. (d) Re-open About, **uncheck** the box, and confirm the Startup-apps entry flips to disabled. (e) Optionally, disable PodBridge from **Task Manager → Startup apps**, then re-open About.
- **Expected:** (a) On first open the box is **unchecked** (default OFF — `Enabled="false"` in the manifest; also guarded by `StartupToggleTests`). (b) Checking it registers the `PodBridgeStartup` startup task; the Startup-apps list shows **PodBridge = Enabled**. (c) After reboot PodBridge **auto-starts to the tray** — the setting **persists across reboot**. (d) Unchecking **clears** it (Startup-apps → Disabled). (e) If the user disabled it in Windows, the checkbox shows unchecked **and disabled**, with the hint `Auto-start is turned off in Windows Settings or Task Manager; change it there.` — the app honestly reflects the user's choice and does not silently re-enable it.
- **Maps to:** issue #35; spec Verification "the auto-start toggle takes effect and persists across a reboot (default OFF)"; the enable/disable/default-OFF logic is device-independently guarded by `StartupToggleTests` (fake `IStartupToggle`).

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.8 asInvoker / app-only at run time (review + smoke) — [machine + real-hardware]
- **Needs:** running installed app + repo.
- **Action:** With PodBridge running, open Task Manager → Details, find `PodBridge.App.exe`; confirm it is **not elevated**. Confirm no driver was installed (no `pnputil` / INF / `.sys` from PodBridge; check the MSIX payload has no `.sys`). Re-read all Phase-5 user-facing strings (About window, tray `About PodBridge`, user docs).
- **Expected:** Runs as a normal (Medium-IL) user process — no admin requested, **no driver/INF/`pnputil`**. The Tier-1 feature set works with **no driver present** (graceful degradation). No string anywhere claims Apple-parity sound or promises to remove the mic trade-off; the disclaimer + honest audio/mic note are present and truthful.
- **Maps to:** spec Verification "the Tier-1 test suite passes with no driver present (graceful degradation), and the packaged app runs `asInvoker`"; constitution honesty + Tier-1 invariants.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

### 5.9 Uninstall leaves no driver / residue — [real-hardware]
- **Needs:** installed MSIX.
- **Action:** Uninstall via **Settings → Apps → Installed apps → PodBridge → Uninstall** (or `Remove-AppxPackage` for the PodBridge package; for a Store install, `winget uninstall <StoreProductId>`). Then check for residue.
- **Expected:** The app uninstalls with **no admin prompt** and **no reboot**. No PodBridge driver/service remains (there never was one — app-only). The only per-user residue is the small settings folder `%LOCALAPPDATA%\PodBridge` (the chosen mic mode `mic-policy-mode.txt` + first-run markers), which the user may delete; the manual-channel trust certificate, if imported, stays in `Cert:\LocalMachine\TrustedPeople` until removed (admin) — both documented in `docs/user/README.md` "Uninstall". No network calls beyond the explicit update check (local-only).
- **Maps to:** issue #37; spec Verification "uninstall via winget/Settings is clean"; user-docs uninstall section.

`[ ] PASS   [ ] FAIL`  Notes: ______________________________

---

## 6. Known limitations / PARKED (documented — do **not** reject Phase 5 for these)

- **The Microsoft Store submission + the no-admin `msstore` install are DEFERRED — issue #38 is PARKED (`blocked:human`).** The headline **no-admin** `winget install <StoreProductId> -s msstore --scope user` **cannot be delivered or verified in Phase 5** because it requires (1) a **Microsoft Partner Center** individual developer account (~$19 one-time), (2) the Partner-Center-derived package identity — the 12-char Store product ID (the `msstore` `PackageIdentifier`) and the Store-matching MSIX `Identity/Name` + `Publisher`, which do not exist until the coined name is reserved, and (3) Microsoft **certification** of the listing, followed by a real-hardware no-admin QA pass on a clean Windows 11 box with AirPods. All three are outside what can be done here. **Until #38 lands, the self-signed manual MSIX (with the one-time admin cert-trust, §5.3) is the only fallback install channel.** The `msstore` entry (`packaging/msstore/msstore-entry.yaml`, `packageIdentifier: STORE_PRODUCT_ID_TBD`, `verified: false`) and the Store-association template are **prepared** here against placeholders only.
- **This QA pass does NOT close milestone #5.** Because #38 is parked, milestone #5 stays open and the spec `docs/specs/spec-packaging-distribution.md` is **not** archived (unlike Phases 1–4, which were fully accepted). This manual documents acceptance of the **driver-free MVP scope that shipped** (issues #31–#37); the Store submission + first public release remain the outstanding, human-gated work.
- **Self-signed MSIX needs an admin cert-trust — by design, not a bug.** A sideloaded self-signed package will not install until its certificate is trusted in the **machine** `TrustedPeople` store (admin). That is intrinsic to Windows MSIX trust; the no-admin path is the Store channel only.
- **Auto-start requires MSIX package identity.** The About auto-start toggle only functions from the **installed MSIX**; an unpackaged dev run shows it OFF/blocked. Behaviour is unit-guarded via the fake `IStartupToggle`; the real registration + reboot-persistence is verified only at the install QA (§5.7).
- **Honest audio + mic caveats restated (unchanged platform limits, not defects):**
  - **AAC vs SBC:** on supported hardware Windows plays media over **AAC** (the best codec available on Windows — not Apple-identical); on hardware/drivers that only negotiate **SBC**, quality is lower. PodBridge reports the negotiated codec truthfully and never offers a "force AAC" button.
  - **A2DP↔HFP mic trade-off:** using the AirPods microphone forces the Bluetooth **HFP** call profile and collapses media to mono call quality. This is a **Bluetooth-Classic platform limit, not a PodBridge bug**; Phase 4 manages *which device* holds the mic role, it does not remove the trade-off.

---

## 7. Recording results & regressions

- Mark each case `PASS` / `FAIL` above (including §4) and keep the Notes for anything unexpected.
- **On any FAIL:** file **one `fix:` issue per finding** in **milestone #5** (normal issue format; place on board **Todo**). Include the case number, exact observed vs. expected string, OS build, install channel, and repro steps. Re-run this guide after the fix merges.
- **On full PASS of the shipped scope (§4, §5.1–5.9):** the **driver-free MVP scope (issues #31–#37) is accepted**. **Milestone #5 stays OPEN and the spec stays UNARCHIVED** because the Store submission + first public release (**issue #38**) are parked (`blocked:human`); §6 is the outstanding work. Do **not** close the milestone or archive the spec on the strength of this pass alone.

---

## 8. Cleanup

- Right-click the tray icon → **Exit**.
- Confirm no lingering process: `tasklist /FI "IMAGENAME eq PodBridge.App.exe"` → `Keine Aufgaben` / `No tasks are running`. If any lingers: `taskkill /IM PodBridge.App.exe`.
- If you installed the MSIX only for testing, uninstall it (§5.9). If you imported the self-signed certificate (§5.3) and want to undo the trust, remove it from `Cert:\LocalMachine\TrustedPeople` (admin).
- If you enabled auto-start for §5.7, turn it back off from **`About PodBridge`** (or Task Manager → Startup apps) unless you want it to persist.
