# Research: MSIX packaging, no-admin signing, `windows.startupTask` & winget/`msstore`

> Permanent record for the `chore:research-msix-packaging` issue (#31).
> Authority for the Phase-5 implementation issues: single-project/`.wapproj` MSIX
> packaging (#34), `windows.startupTask` auto-start (#35), the release workflow +
> self-signed GitHub-Releases MSIX + `msstore` manifest prep (#36), and the Store
> submission / MVP release (#38).
>
> Clean-room (Apache-2.0): this file records **facts only** — MSBuild property
> names/values, manifest element/attribute names, the `StartupTask` WinRT method
> set, `signtool`/PowerShell command shapes, and the `winget`/`msstore` command
> forms — described in my own words from Microsoft Learn plus the official
> `microsoft/winget-cli` repo. No GPL source or verbatim protocol/doc prose is
> reproduced beyond short, unavoidable API/CLI tokens.
>
> Scope: confirm, from ≥ 3 authoritative sources, how to **package + sign +
> distribute + auto-start** `PodBridge.App` (a WPF/.NET 10 tray app) so the
> driver-free MVP installs with **no admin rights**, built headlessly in CI on
> `windows-latest`, keeping the `asInvoker` manifest and the local-only ethos.

## Sources

1. [Microsoft Learn — Package a .NET app with MSIX (WPF/WinForms)](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/dotnet/package-app)
   — **authoritative** for the *non-WinUI* desktop path. Microsoft's documented
   way to package a WPF/WinForms app is a **Windows Application Packaging Project**
   (`.wapproj`) that references the app project; you must delete
   `<WindowsPackageType>None</WindowsPackageType>` from the app `.csproj` so it
   gains package identity.
2. [Microsoft Learn — Package your app using single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
   — **authoritative** for single-project MSIX. Adds `<EnableMsixTooling>true</EnableMsixTooling>`
   + a `<PublishProfile>` to the app project and moves `Package.appxmanifest` +
   `Images/` into it. States it **"lets you build a packaged WinUI 3 desktop app"**,
   its **"Supported project types"** are **WinUI templates** (C#/C++), and it
   supports only **a single executable**. Headless build: `msbuild … /p:GenerateAppxPackageOnBuild=true`;
   links a working WinUI single-project GitHub Action.
3. [Microsoft Learn — `desktop:StartupTask` element (Appx manifest schema)](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask)
   — **authoritative** for the manifest element: attributes `TaskId` (required),
   `Enabled` (optional bool), `DisplayName` (optional), `rescap5:ImmediateRegistration`;
   namespace `http://schemas.microsoft.com/appx/manifest/desktop/windows10`; min OS
   Windows 10 1607.
4. [Microsoft Learn — `StartupTask` class (`Windows.ApplicationModel`)](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100)
   — **authoritative** for the WinRT API: extension `Category="windows.startupTask"`,
   `Executable`, `EntryPoint="Windows.FullTrustApplication"`; methods `GetAsync`,
   `GetForCurrentPackageAsync`, `RequestEnableAsync`, `Disable`; `State` +
   `StartupTaskState` (`Disabled`/`DisabledByUser`/`DisabledByPolicy`/`Enabled`).
   For a **packaged desktop app** `RequestEnableAsync` shows **no consent dialog**;
   it will **not** override a user's Task-Manager disable.
5. [Microsoft Learn — Code signing options for Windows app developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
   — **authoritative** for signing/trust tiers: **Store (MSIX)** re-signed free,
   no warnings; **Azure Artifact Signing** (~$9.99/mo, US/CA individuals), **OV**
   ($150–300/yr), **EV** (no longer bypasses SmartScreen), **self-signed** (blocks
   public install unless manually trusted), **SignPath Foundation** (free OV-level
   for OSS).
6. [Microsoft Learn — Create a certificate for package signing](https://learn.microsoft.com/en-us/windows/msix/package/create-certificate-package-signing)
   — **authoritative**: the certificate **Subject must match the `Publisher`** in
   the manifest `Identity`; `New-SelfSignedCertificate` code-signing EKU
   `1.3.6.1.5.5.7.3.3`; to install, **import into `Cert:\LocalMachine\TrustedPeople`
   via `Import-PfxCertificate` from an admin session** — affects all users.
7. [Microsoft Learn — Configure CI/CD pipeline with YAML (MSIX)](https://learn.microsoft.com/en-us/windows/msix/desktop/azure-dev-ops)
   — **authoritative** for the headless MSBuild + sign flow: builds `Msix.wapproj`
   with `/p:UapAppxPackageBuildMode=SideLoadOnly /p:AppxBundle=Never
   /p:AppxPackageDir=… /p:AppxPackageSigningEnabled=…`; signs with
   `signtool sign /fd SHA256 /f certificate.pfx /p <pwd> App.msix`; PFX comes from a
   secure file, not the repo.
8. [Microsoft Learn — The WinGet `source` command](https://learn.microsoft.com/en-us/windows/package-manager/winget/source)
   — **authoritative**: default sources `msstore` / `winget` / `winget-font`;
   `msstore` = **the Microsoft Store catalog** at
   `https://storeedgefd.dsx.mp.microsoft.com/v9.0`.
9. [Microsoft Learn — The WinGet `install` command](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
   — **authoritative** for the `msstore` install form: **"The `msstore` source uses
   unique identifiers as the Id"** — e.g. `winget install XP9KHM4BK9FZ7Q -s msstore`
   — and these **"do not require the `exact` query option"**. `--scope user|machine`.
10. [microsoft/winget-cli #3271 — msstore package identifier in manifests](https://github.com/microsoft/winget-cli/issues/3271)
    and [#3048 — moniker for msstore apps](https://github.com/microsoft/winget-cli/issues/3048)
    — the community `winget` repo **cannot host a YAML manifest for an `msstore`
    app**; the Store product ID is passed directly. Corroborates that an `msstore`
    listing is only resolvable **after** the Store submission is published/certified.

## Consensus

### 1. Single-project MSIX packaging for a WPF/.NET 10 app

- **What single-project MSIX is (source 2):** in the *app* project's main
  `<PropertyGroup>` add `<EnableMsixTooling>true</EnableMsixTooling>` and
  `<PublishProfile>Properties\PublishProfiles\win10-$(Platform).pubxml</PublishProfile>`,
  then move `Package.appxmanifest` and the `Images/` folder to the app project
  root (`Build Action = Content`), and remove any separate packaging project. It
  emits **one MSIX** (no bundle, single executable).
- **Headless build (source 2):** the load-bearing MSBuild switch is
  **`/p:GenerateAppxPackageOnBuild=true`** — "Without that option, the project will
  build, but you won't get an MSIX package." Output dir via
  `/p:AppxPackageDir=…`. Building the package requires **MSBuild + the MSIX
  packaging targets** (present on the `windows-latest` GitHub runner via the VS
  build tools/WinUI+UWP workloads); a bare `dotnet build`/`dotnet publish` on the
  SDK alone does not produce an MSIX.
- **Official scope caveat (sources 1 vs 2):** Microsoft documents single-project
  MSIX for **WinUI 3** only ("Supported project types: WinUI templates"). The
  **documented WPF/WinForms path is the Windows Application Packaging Project
  (`.wapproj`)** (source 1), and the only Microsoft CI sample that builds+signs a
  desktop MSIX headlessly builds a `.wapproj` (source 7). See **Disputes** — this
  diverges from the spec's single-project default and must be settled at the
  spec-acceptance gate.
- **`asInvoker` invariant:** packaging does not change the run-time integrity
  level. Keep `<uap:Application>`'s executable `asInvoker`; declare **no**
  restricted/elevated capabilities. MSIX runs the packaged WPF app unelevated.
- **`WindowsPackageType`:** for the `.wapproj` path, the app `.csproj` must **not**
  carry `<WindowsPackageType>None</WindowsPackageType>` (source 1 has you delete
  it) so the app gains package identity from the packaging project.
- **Identity in the manifest:** `Package.appxmanifest` `<Identity Name="…"
  Publisher="CN=…" Version="A.B.C.D"/>`. `Name`+`Publisher` are the
  Partner-Center-derived Store values for the Store channel (§4); the self-signed
  GitHub-Releases build uses its own throwaway `Name`/`Publisher` whose `Publisher`
  string **must equal the signing cert Subject** (§2, source 6).

### 2. No-admin install reality — signing & trust (the headline invariant)

- **The rule (sources 5, 6):** an MSIX installs for a normal user only if its
  signing certificate is **already trusted on the machine**. Three ways to get
  there, and only the first is both free-to-user and admin-free:
  - **Store-signed (source 5):** publish the MSIX through the Microsoft Store;
    **Microsoft re-signs it for free**, the Store trust chain is pre-installed on
    every Windows 11 machine, so install via the Store / `winget … -s msstore`
    needs **no admin and no cert step**. This is PodBridge's **no-admin path**.
  - **Publicly-trusted CA cert** (Azure Artifact Signing ~$9.99/mo, or OV
    $150–300/yr, or free **SignPath Foundation** for OSS — source 5): chains to a
    CA in the Trusted Root program, so a sideloaded MSIX installs **without** a
    manual trust step. Costs money / eligibility; not Store-eligible signing.
  - **Self-signed (source 6):** the cert is **not** trusted by default. Before the
    MSIX can install, someone must
    **`Import-PfxCertificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople`
    from an admin PowerShell session** (App Installer checks the *machine* store,
    not the user store). **That one-time trust step requires admin** and affects
    all users — so the self-signed GitHub-Releases MSIX is a **documented manual
    fallback, not the no-admin path.**
- **Does installing the MSIX itself need admin?** No — once the cert is trusted,
  the MSIX installs **per-user** without elevation (double-click App Installer, or
  `winget install`). Admin is only ever needed for the *self-signed cert trust
  step*, never for the Store channel and never for the app at run time.
- **Cert↔manifest coupling (source 6):** the cert **Subject must exactly match**
  the manifest `Identity/Publisher` or signing fails. In CI, `signtool sign /fd
  SHA256 /f cert.pfx /p <pwd> App.msix` or MSBuild
  `/p:AppxPackageSigningEnabled=true /p:PackageCertificateKeyFile=…
  /p:PackageCertificatePassword=…` with `PackageCertificateThumbprint` empty or
  matching (source 7).

### 3. Auto-start at login via `windows.startupTask` (default OFF)

- **Manifest (sources 3, 4)** — a packaged desktop-app startup task:
  ```xml
  <Extensions>
    <uap5:Extension
      Category="windows.startupTask"
      Executable="PodBridge.App.exe"
      EntryPoint="Windows.FullTrustApplication">
      <uap5:StartupTask
        TaskId="PodBridgeStartup"
        Enabled="false"
        DisplayName="PodBridge" />
    </uap5:Extension>
  </Extensions>
  ```
  `Category` **must** be `"windows.startupTask"`; `EntryPoint` **must** be
  `"Windows.FullTrustApplication"`; `Executable` is the packaged exe. Use the
  `uap5` namespace (`…/appx/manifest/uap/windows10/5`) on modern targets, or the
  `desktop` namespace on 1703 (source 4). `TaskId` is the handle the WinRT API
  uses. `DisplayName` is what the user sees on the Task-Manager **Startup** tab.
- **Default OFF:** set `Enabled="false"` in the manifest. (For packaged desktop
  apps `Enabled="true"` would enable-at-first-launch without an API call; the spec
  wants **default OFF**, so `false`.)
- **WinRT toggle (source 4):**
  - `var task = await StartupTask.GetAsync("PodBridgeStartup");` → read
    `task.State`.
  - **Enable:** `await task.RequestEnableAsync();` — for a **packaged desktop app
    no consent dialog is shown** (differs from UWP, which shows one). Returns the
    resulting `StartupTaskState`.
  - **Disable:** `task.Disable();`.
  - `StartupTaskState`: `Disabled` (off, re-enablable), `DisabledByUser` (user
    turned it off in Task Manager — **`RequestEnableAsync` will not override this**,
    the user must re-enable manually), `DisabledByPolicy`, `Enabled`.
- **User is always in control (source 4):** once enabled, the user can flip it any
  time via **Settings → Startup** or the **Task Manager → Startup** tab; the app
  must respect a `DisabledByUser` state and surface guidance rather than fight it.
- **Architecture fit:** wrap all of the above behind the `Core` `IStartupToggle`
  interface with a `PodBridge.Windows` adapter (`StartupTaskToggle`); the About
  view-model calls the interface. Unit-test the toggle logic with a fake
  `IStartupToggle` (default OFF; enable→registered; disable→cleared) — no packaged
  runtime needed. Real behaviour (persists across reboot) is a human-QA-gate item.

### 4. winget `msstore` source, Store listing & package identity

- **`msstore` install form (sources 8, 9):** `msstore` is the Microsoft Store
  catalog (`https://storeedgefd.dsx.mp.microsoft.com/v9.0`). Install a Store app
  by its **Store product ID**: `winget install <ProductId> -s msstore` — the doc's
  own example is `winget install XP9KHM4BK9FZ7Q -s msstore`, and Store IDs **do
  not require `--exact`**.
- **`PackageIdentifier` form:** the `msstore` identifier **is** the Store product
  ID — a **12-character alphanumeric** string, typically starting `9`/`XP`
  (prior-art MagicPods = `9P6SKKFKSHKM`; docs example `XP9KHM4BK9FZ7Q`,
  `9WZDNCRFHVJL`). It is **not** the `Publisher.Package` form the community
  `winget` source uses.
- **No community manifest for Store apps (source 10):** you **cannot** submit a
  YAML manifest to `microsoft/winget-pkgs` that points at an `msstore` app; the
  Store product ID is resolved directly by the `msstore` source. So "preparing the
  `msstore` entry" = recording the product ID + install command in docs/release
  notes, **not** authoring a winget-pkgs PR.
- **Listing-resolution timing:** the Store product ID exists only after the human
  **reserves the name in Partner Center**, and `winget install … -s msstore`
  resolves it only **after the submission is certified/published** (sources 9, 10).
  Hence the spec correctly **defers the verified no-admin `msstore` install to the
  human-QA gate after certification** (#38), while #36 only prepares the
  entry/manifest + the self-signed GitHub-Releases fallback.
- **Where the identity values come from (Partner Center):** reserving the coined
  product name yields (a) the **Store product ID** (the `msstore`
  `PackageIdentifier`); (b) the MSIX **`Identity/Name`** and the assigned
  **`Publisher`** (`CN=…`, from the Partner Center account) plus **Publisher
  display name** — these must be written into `Package.appxmanifest` for the
  **Store channel**, and are shown greyed-out by VS "Publish → Associate App with
  the Store". The **`Publisher` is the certificate Subject**; on the Store channel
  Microsoft's re-sign supplies it, so the local Store-channel manifest just has to
  match the reserved identity. The self-signed GitHub-Releases MSIX uses its own
  throwaway `Identity` and does **not** depend on these values.

### 5. CI on `windows-latest`: build + sign both channels

- **Build (sources 2, 7):** run **MSBuild** (not bare `dotnet build`) with the
  packaging targets: `/p:GenerateAppxPackageOnBuild=true` (single-project) or build
  the `.wapproj` with `/p:UapAppxPackageBuildMode=SideLoadOnly /p:AppxBundle=Never
  /p:AppxPackageDir=<artifacts>`. `windows-latest` ships MSBuild + the MSIX
  targets; publish the `.msix` as a workflow artifact.
- **Sign the GitHub-Releases fallback (sources 6, 7):** feed the **self-signed
  PFX from a GitHub Actions secret** (never committed), then either
  `AppxPackageSigningEnabled=true` + `PackageCertificateKeyFile`/`Password`, or a
  post-build `signtool sign /fd SHA256 /f $env:PFX /p $env:PWD PodBridge.App.msix`.
  The PFX Subject must match the fallback manifest `Publisher`. Document the
  one-time admin `Import-PfxCertificate … Cert:\LocalMachine\TrustedPeople` trust
  step for users of this channel.
- **Store channel (source 5):** submit the MSIX unsigned-by-you to Partner Center;
  **Microsoft re-signs** — no signing secret and no cert management for the
  no-admin path. (Store MSIX submissions need no publisher Authenticode cert; only
  MSI/EXE Store submissions do.)
- **Verify stays green:** none of this touches `PodBridge.Core`/`.App` source or
  `build/verify.ps1`; the research is docs-only. The packaging MSBuild runs in the
  release/packaging workflow, separate from the Verify gate.

## Disputes (minority → majority decision)

- **Single-project MSIX (`EnableMsixTooling`) vs Windows Application Packaging
  Project (`.wapproj`) for a *WPF* app.** The spec's DEFAULT (and issue #34's
  title) is **single-project MSIX**, on the stated rationale that it is
  "dotnet/CI-friendly … without Visual Studio-only project types." The
  authoritative sources partly contradict that rationale: single-project MSIX is
  **documented for WinUI 3 only** (source 2, "Supported project types: WinUI
  templates"), its tooling ships as a **VS extension**, and Microsoft's documented
  *WPF* MSIX path is the **`.wapproj`** (source 1), which is also the only
  first-party **headless CI** build+sign sample (source 7). Both paths ultimately
  require **MSBuild + the MSIX packaging targets** (both present on
  `windows-latest`); neither is a pure `dotnet` SDK build. → **Majority/decision:**
  treat the **`.wapproj` (Windows Application Packaging Project) as the safe,
  officially-supported, CI-proven default for the WPF app**, and only use
  single-project `EnableMsixTooling` if #34 verifies it builds+signs a WPF MSIX
  headlessly on `windows-latest`. This **diverges from the spec's single-project
  default** — flag it for confirmation at the spec-acceptance gate; either way the
  outcome (one signed MSIX, `asInvoker`, built in CI) and every other issue are
  unaffected. *Uncertainty marked, sources not smoothed over.*
- **`RequestEnableAsync` shows a consent dialog?** A general search snippet
  suggested "call `RequestEnableAsync` … to trigger a user-consent dialog." That is
  the **UWP** behaviour. → **Decision (source 4, authoritative):** for a **packaged
  desktop app** (our case) `RequestEnableAsync` shows **no consent dialog**; the
  toggle enables silently. The user's Task-Manager/Settings override still wins
  (`DisabledByUser` is not overridable by the API).
- **`uap5:` vs `desktop:` namespace for the StartupTask extension.** Source 3 (the
  schema element page) documents `desktop:StartupTask`; source 4's example uses
  `uap5:`. → **Decision:** functionally equivalent; **use `uap5:`** (the general
  UAP contract v5 namespace) on the Win11 21H2+ target, per source 4's example and
  the modern packaging templates. `desktop:` is the 1703-era spelling.
- **Does an MSIX install require admin?** Conflicting community answers exist
  (some report `0x80070005`/UAC on machine-wide installs). → **Decision (sources
  5, 6):** the **Store channel installs per-user with no admin**; a **self-signed
  sideload needs admin exactly once** to trust the cert in
  `LocalMachine\TrustedPeople`, after which the per-user install is admin-free.
  This is precisely why the Store channel is the headline no-admin path and the
  self-signed MSIX is a documented fallback.
- **Can the `msstore` entry be a `winget-pkgs` manifest?** Minority assumption:
  author a YAML manifest like a normal winget package. → **Decision (sources 9,
  10):** **no** — `msstore` apps are installed by their Store product ID directly;
  there is no community manifest. "Preparing the `msstore` entry" means recording
  the product ID + `winget install … -s msstore` command, resolvable only after
  Store certification.

Closes #31.
