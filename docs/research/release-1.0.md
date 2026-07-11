# Research: Release 1.0 — self-contained `.exe` distribution + security hardening

> Created: 2026-07-11. This is the **content authority** for the release-1.0
> implementation issues (spec `docs/specs/spec-release-1.0.md`). It was produced
> at planning time by a multi-source research pass (5 parallel researchers +
> synthesis), which satisfies the workflow contract's research-intensive rule:
> implementers read this file instead of re-running WebSearch. Confidence and
> caveats are preserved per fact; anything marked *measure/confirm* is an
> implementation-time check, not a settled value.

## 1. Self-contained single-file publish (.NET 10 / WPF)

- **Minimal working combo** (high): `PublishSingleFile=true` + `SelfContained=true`
  + `RuntimeIdentifier=win-x64|win-arm64`. Setting a RID alone already flips
  `SelfContained=true`. Single-file apps are **OS+arch specific** — publish once
  per RID; there is no one exe for both x64 and arm64.
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview>
- **True "one file"** (high): only managed DLLs are embedded by default; native
  runtime DLLs stay loose unless `IncludeNativeLibrariesForSelfExtract=true`,
  which embeds them and self-extracts before start — this gives the
  download-one-file-and-run behaviour we need. (Same source.)
- **Trimming is a hard NO for WPF** (high): "almost no WPF apps are runnable after
  trimming, so trimming support for WPF is currently disabled in the .NET SDK";
  attempting `PublishTrimmed` errors **NETSDK1168**. Settled constraint, not a
  choice — never set `PublishTrimmed`.
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities>
- **ReadyToRun** (medium): `PublishReadyToRun=true` precompiles IL → native for
  faster startup; self-contained only (fine). `win-arm64` R2R **cross-compiles
  from the x64 `windows-latest` runner** via crossgen2 — no arm64 runner needed.
  <https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run>
- **Compression** (high): `EnableCompressionInSingleFile=true` shrinks download at
  the cost of decompression on **every** start; Microsoft says measure per-app.
  → *Measure* cold+warm startup and size for R2R-on / compression-on / both on the
  first real publish before locking the choice.
- **Size expectation** (medium): a self-contained single-file WPF exe bundles the
  full runtime + WPF/WinRT assemblies — order-of-magnitude tens of MB up to
  ~150 MB (one dotnet/wpf issue reports 150 MB uncompressed). **Do not assume a
  number — measure PodBridge's own output.**
  <https://github.com/dotnet/wpf/issues/3070>
- **Storage is already portable-safe** (high): several files
  (`MicPolicyModeStore`, `FirstRunGuidanceState`, `RollingFileLoggerProvider`,
  `DiagnosticsFileSystemInterop`, `AdvancedTierInstallInterop`, `GestureConfigStore`)
  already use `Environment.SpecialFolder.LocalApplicationData`
  (`%LOCALAPPDATA%\PodBridge`) for settings/logs/diagnostics/gesture-config —
  never the exe dir. No change needed for the pivot.
- **Deterministic/reproducible build** (high): `Deterministic=true` is the SDK
  default; pass `-p:ContinuousIntegrationBuild=true` **only in CI** (locally it
  bakes CI paths into PDBs and breaks debugging). Add SourceLink /
  `DotNet.ReproducibleBuilds` and publish commit-mapped PDBs.
  <https://github.com/dotnet/reproducible-builds>
- **SDK gotcha** (medium): .NET 10 `dotnet publish` may ignore
  `--self-contained false` when `PublishSingleFile=true` is also set — irrelevant
  for our self-contained-only plan, but verify actual output.
  <https://github.com/dotnet/sdk/issues/51888>

**Recommended csproj block (scoped so the plain `dotnet build`/Verify path is untouched):**

```xml
<PropertyGroup Condition="'$(RuntimeIdentifier)' != ''">
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  <PublishReadyToRun>true</PublishReadyToRun>
  <!-- EnableCompressionInSingleFile: decide by measurement (size vs startup). -->
  <!-- Never set PublishTrimmed for this WPF project — NETSDK1168 / broken app. -->
</PropertyGroup>
```

Publish per arch: `dotnet publish src/PodBridge.App -c Release -r win-x64 -p:ContinuousIntegrationBuild=true` (and again `-r win-arm64`).

## 2. SmartScreen reality + code-signing options

- **Unsigned + Mark-of-the-Web** (high): a browser download tags the file (MOTW);
  running an unsigned exe triggers SmartScreen "Windows protected your PC /
  unrecognized app / **Unknown publisher**"; user clicks More info → Run anyway.
  Enterprise policy can disable "Run anyway" entirely.
  <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation>
- **Self-signed = no benefit** (high): MS Learn table lists self-signed as "same
  behavior as no signature"; a secondary source calls it a malware heuristic
  (arguably worse than unsigned). Do **not** self-sign the release exe.
- **EV no longer buys instant trust** (high, key correction): "That behavior was
  removed in 2024. EV-signed files now go through the same reputation-building
  process as OV… Paying the EV premium ($400+/yr) solely to avoid SmartScreen is
  no longer justified." **No signing option gives instant first-download trust
  except the Microsoft Store (which 1.0 excludes).** Reputation builds over
  "several weeks and hundreds of clean installs."
- **Azure Artifact Signing** (medium; renamed from Trusted Signing): Basic
  ~**$9.99/mo** (~$120/yr), **no hardware token**, CI-native (GitHub Actions),
  publicly-trusted signature — but **individual tier is USA/Canada only**; the org
  path needs a ~3-year legal-entity history. Identity validation takes a few
  business days. Gives the *gradual* OV-style reputation, **not** instant trust.
- **OV cert from a CA** (medium): ~$150–300/yr (resellers up to ~$550);
  geography-independent, but CA/B Forum rules since 2023-06 force a FIPS
  HSM/USB token even for OV — awkward for CI vs Azure's tokenless flow.
- **SignPath Foundation** (medium): **free** OV-equivalent signing for qualifying
  OSS, **no residency limit**. Requires: OSI license, no proprietary components,
  actively maintained, **already released in the form to be signed**, documented,
  and a **verifiable/reproducible build**. Apply via their OSS Request Form;
  budget for vetting turnaround. PodBridge (Apache-2.0, public) plausibly
  qualifies. <https://signpath.org/terms.html>
- **Smart App Control** (high): a *separate*, stricter Win11 mechanism that can
  block **unsigned** executables outright regardless of reputation — some
  locked-down users cannot run an unsigned exe at all.

> Bottom line: signing is a real **spend/identity decision**, resolved at the
> spec-acceptance gate. The free supply-chain baseline below is the trust backbone
> either way; signing only removes "Unknown publisher" and accelerates reputation.

## 3. Free supply-chain security baseline (GitHub-native, $0 for a public repo)

- **Build-provenance attestation** (high): a release workflow with
  `permissions: { id-token: write, contents: read, attestations: write }` calls
  `actions/attest-build-provenance` (subject = each published exe) to emit a
  signed provenance record tied to repo/workflow/commit/build. Users verify with
  `gh attestation verify <exe> -R <owner>/<repo>` — **fails closed** if the binary
  isn't a real CI build. Strictly stronger than a same-page checksum.
  <https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations>
- **SHA-256 checksums** (high): GitHub now exposes an immutable per-asset SHA-256
  digest (since 2025-06); still best practice to publish a `checksums.sha256` file
  and tell users to verify (`certutil -hashfile app.exe SHA256`) over HTTPS from
  the official release page.
  <https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/>
- **SBOM** (standard): generate a CycloneDX SBOM for .NET in CI and attach it to
  the release. *Confirm exact tool at implementation* (e.g. the CycloneDX .NET
  tool / `CycloneDX/gh-dotnet-generate`).
- **CodeQL (C#)** (standard GitHub-native): code-scanning workflow on push/PR;
  C# analysis needs a build step (`dotnet build` against the slnx, or autobuild).
- **Dependabot** (standard): `.github/dependabot.yml` for `nuget` + `github-actions`
  ecosystems (version + security updates).
- **Dependency review** (standard): `actions/dependency-review-action` on PRs,
  failing on known-vulnerable dependencies (pairs with `dotnet list package
  --vulnerable`).
- **Workflow hardening** (standard): least-privilege top-level `permissions:`
  blocks, **pin third-party actions to full commit SHAs**, no broad token scopes.

> The CodeQL/Dependabot/dependency-review specifics are standard, well-documented
> GitHub features (one research sub-agent returned a stub for this topic; the
> synthesis filled them from general knowledge). They are low-risk and free;
> **confirm exact action versions/SHAs at implementation.**

## 4. Portable auto-start (no admin, no MSIX)

- **HKCU Run key** (high): `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`
  — per-user, **never needs elevation** (only the HKLM twin does). Value = a
  command line; **cap 260 chars**. Uses only `Microsoft.Win32.Registry` (pure
  BCL, single-file-safe). <https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys>
- **Startup-folder shortcut** (alt): per-user `shell:startup`, no admin, but
  writing a `.lnk` needs COM/`IShellLink` (fragile in single-file) or a 3rd-party
  dep — worse fit than the Run key.
- **MSIX `windows.startupTask` requires package identity** (high) — does **not**
  work for an unpackaged exe; the existing `StartupTaskToggle` already degrades to
  `Disabled` when unpackaged. So a new adapter is required.
- **User-disable respect** (medium): disabling a startup item in Task Manager
  writes a flag under `…\Explorer\StartupApproved\Run` (undocumented binary,
  byte `0x03` = disabled) — the original value stays but inert. Reading it lets
  the adapter report `DisabledByUser` honestly (preserving the existing
  `StartupToggleState` contract), but the format is community-reverse-engineered.
- **Stale-path problem** (high, no OS warning): if the user moves the portable
  folder and never relaunches from the new location, a stale Run value silently
  won't fire. Mitigate by **rewriting the value's path on every enable** and
  **self-healing on each launch while Enabled** (compare stored path to
  `Environment.ProcessPath`, silently rewrite on mismatch). Document the
  moved-and-never-relaunched edge as a known limitation.
- **Threat-model note** (medium): HKCU Run and Startup folder are both MITRE
  ATT&CK **T1547.001** — equally monitored; neither is "more suspicious." Keeping
  it default-OFF, opt-in, per-user, user-revocable is the right posture.

## 5. Security docs + disclosure + BLE-input hardening

- **SECURITY.md + PVR** (high): SECURITY.md = supported-versions table (latest tag
  only) + "report via GitHub Private Vulnerability Reporting." Enable PVR:
  Settings → Security → Private vulnerability reporting → Enable (one checkbox,
  free, owner/admin). Triage privately → GHSA/release-note disclosure; request a
  CVE only if a reporter asks.
  <https://docs.github.com/en/code-security/getting-started/adding-a-security-policy-to-your-repository>
- **Threat model** (for THREAT-MODEL.md): local-only, no-admin, no-driver by
  default. Two surfaces that matter: (a) **untrusted BLE advertisement bytes** and
  (b) the **download/release supply chain** (mitigated by attestation + checksums
  + SBOM). Auto-start is default-OFF and user-revocable.
- **BLE input is a real RCE-class surface** (medium): **CVE-2023-24871** was an
  integer overflow in *Windows' own* BLE advertisement parser (8-bit section
  counter overflow → undersized heap buffer → OOB write → RCE) driven by
  attacker-controlled length/count fields from any nearby device — directly
  analogous to PodBridge's Continuity/AAP byte parsing.
  <https://ynwarcs.github.io/z-btadv-cves>
- **Existing coverage** (high): `ContinuityParser` is already a fixed-offset,
  bounds-checked decoder with `ContinuityParserFuzzTests` in the tree. So this is
  **hardening/documentation of existing coverage**, not greenfield: extend fuzz
  coverage for malformed length/count fields and document the untrusted-input
  boundary.
- **No BinaryFormatter** (high): `BinaryFormatter` is "insecure and can't be made
  secure" and throws in .NET 9+. Confirm all local config/diagnostics parsing uses
  `System.Text.Json`. <https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide>
- **Single-file extraction dir** (high): native libs self-extract to `%TEMP%\.net`
  (override: `DOTNET_BUNDLE_EXTRACT_BASE_DIR`). MS security note: the extraction
  dir "shouldn't be writable by users or services with different privileges."
  Verify the default is per-user; cover local-tampering in the threat model.

## Open decisions (resolved at the spec-acceptance gate — see the spec)

1. **Signing strategy** — free+provenance (recommended; apply to SignPath) vs
   Azure Artifact Signing (~$120/yr, US/Canada individual only) vs OV cert
   (~$150–300/yr + HSM). Self-signed / EV explicitly ruled out.
2. **MSIX/Store disposition** — archive-and-supersede (recommended) vs rip out vs
   keep dormant.
3. **Target architecture** — x64+arm64 (recommended) vs x64-only (fewer assets to
   QA; arm64 QA needs arm64 hardware).
