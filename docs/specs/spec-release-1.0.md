# Spec: Release 1.0 — self-contained `.exe` + security hardening

> Created: 2026-07-11

Ship PodBridge **1.0** as a **self-contained, single-file `.exe`** downloaded from
GitHub Releases and **run directly — no MSIX, no Microsoft Store, no installer,
no admin** — with a **security posture built for downloadable software**
(build-provenance attestation, published checksums, SBOM, CodeQL, Dependabot,
dependency review, hardened + pinned workflows, a reproducible build, a
`SECURITY.md` + private vulnerability reporting, and a written threat model).
This **supersedes the distribution mechanism of Phase 5** (MSIX + Store), which
never shipped because it was blocked on a paid Microsoft Partner Center account.
It adds **no new device feature**; every Tier-1/Tier-2 capability from Phases 1–8
is packaged and shipped as-is. This spec carries no lifecycle state — acceptance
is the spec merged on the default branch with a milestone and issues; a completed
spec is moved to `docs/specs/archive/`.

> **This spec's PR also amends two permanently-loaded foundation docs**, because
> the direction contradicts their current text and would otherwise mislead every
> future context and implementer: `docs/constitution.md` (packaging row) and
> `docs/vision.md` (distribution success-criterion). Those edits are ratified
> together with this spec at the acceptance gate.

## Outcome

- [ ] `PodBridge.App` publishes a **self-contained, single-file `.exe`** per
      target RID (`win-x64`, and `win-arm64` if the architecture decision selects
      it) via `PublishSingleFile` + `SelfContained` +
      `IncludeNativeLibrariesForSelfExtract` + `PublishReadyToRun`, scoped so the
      plain `dotnet build`/**Verify path stays green and unchanged**. The exe
      **runs `asInvoker`, needs no admin, no install, and no sidecar files.**
- [ ] **All three loose payload files — `LICENSE`, `NOTICE`, and
      `THIRD-PARTY-NOTICES.md` — are converted from `<Content>` sidecars to
      embedded resources** (read via `Assembly.GetManifestResourceStream`, keeping
      the existing `FallbackNotices` constant), so the single `.exe` ships its own
      Apache-2.0 license text + NOTICE attribution + third-party notices with **no
      file next to the exe** (Apache-2.0 §4); the About window still renders the
      disclaimer, Apache-2.0 notice, third-party notices, version, and honest audio
      note from the embedded copy.
- [ ] The **opt-in auto-start-at-login** toggle works for the portable exe via a
      **per-user `HKCU\…\Run` entry** (no admin), **default OFF**, wired to the
      existing `IStartupToggle` contract; it **rewrites the stored path on enable
      and self-heals a stale path on launch** while Enabled, and honours a
      user's Task-Manager disable. The dead MSIX `StartupTaskToggle` is removed.
- [ ] A **tag-triggered release workflow** on `windows-latest` publishes the
      per-RID single-file exe(s), a `checksums.sha256`, and an **SBOM**, produces
      a **build-provenance attestation** (`actions/attest-build-provenance`)
      verifiable with `gh attestation verify`, and attaches all assets to a
      **GitHub Release** with **honest release notes** (driver-free Tier-1 scope,
      expected first-run SmartScreen behaviour, how to verify the download).
- [ ] The release build is **reproducible/verifiable**: `Deterministic` on,
      `ContinuousIntegrationBuild` **CI-only**, SourceLink /
      `DotNet.ReproducibleBuilds`, commit-mapped PDBs published.
- [ ] The release exe ships **unsigned-with-provenance** (attestation + checksums
      + SBOM as the trust anchor) with a **SignPath Foundation** application pursued
      for a free verified-publisher signature — never self-signed; the release notes
      set honest first-run-SmartScreen expectations.
- [ ] **Repo security posture exists and is enforced:** CodeQL (C#) scanning,
      Dependabot (`nuget` + `github-actions`), **secret scanning + push
      protection**, a dependency-review/vulnerable-package gate on PRs, and **all
      workflows hardened** (least-privilege `permissions:`, third-party actions
      pinned to commit SHAs).
- [ ] A root **`SECURITY.md`** (supported-versions + report-via-PVR) exists,
      **Private Vulnerability Reporting is enabled**, and a **`THREAT-MODEL.md`**
      documents the local-only/no-admin/no-driver posture, the untrusted-BLE-input
      surface (with `ContinuityParser` + fuzz tests as mitigation, citing
      CVE-2023-24871), the download/supply-chain surface (attestation/checksums/
      SBOM), the single-file `%TEMP%\.net` extraction consideration, and the
      default-OFF, user-revocable auto-start.
- [ ] **BLE-input hardening is confirmed:** `ContinuityParser` fuzz coverage is
      extended for malformed length/count fields (no OOB read), and no
      `BinaryFormatter` is used on any local file (all `System.Text.Json`).
- [ ] **User docs are pivoted** to download-and-run: README quickstart + `docs/user`
      describe downloading the exe, the **≤ 2-minute** fresh-run-to-battery-visible
      flow, an honest **"Verify your download"** section (checksum + attestation +
      expected SmartScreen warning, never a blanket bypass), the mic-profile modes,
      the auto-start toggle, and where settings/logs live (`%LOCALAPPDATA%`).
- [ ] The **Phase-5 MSIX/Store work is dispositioned** per the accepted decision
      (default: archived & superseded — packaging project/workflow/manifest/msstore
      artifacts removed, `spec-packaging-distribution.md` archived, `architecture.md`
      updated). The **one open Phase-5 issue #38 is closed as superseded** (not
      completed); the already-closed MSIX issues stay closed as historical.
      **`packaging.yml` must be removed/disabled before or with the `v1.0.0` tag**
      so it does not race the new release workflow to create the same GitHub
      Release (both trigger on `v*`).
- [ ] The **1.0 release is cut** (tag `v1.0.0`, GitHub Release with the above
      assets + notes), `docs/roadmap.md`/`docs/architecture.md` reflect the
      shipped state, and the **Tier-1 suite passes with no driver present**.
- [ ] **Verify stays green** throughout (`build/verify.ps1`), and the
      **constitution + vision amendments** in this spec's PR are merged.

## Scope

### In scope

- Self-contained single-file publish config for `PodBridge.App` (scoped MSBuild
  props; `win-x64` + optionally `win-arm64`; R2R; measured compression choice;
  never trimmed) and embedding the three bundled legal files (`LICENSE`, `NOTICE`,
  `THIRD-PARTY-NOTICES.md`) as resources so the exe is sidecar-free.
- A portable **`HKCU\…\Run`** auto-start adapter behind `IStartupToggle` (default
  OFF, path self-heal), replacing the MSIX `StartupTaskToggle`.
- A tag-triggered **release workflow**: per-RID publish, `checksums.sha256`, SBOM,
  build-provenance attestation, reproducible-build flags, GitHub Release + notes.
- **Code signing** of the release exe per the gate-accepted strategy.
- **Repo/CI security:** CodeQL, Dependabot, dependency review / vulnerable-package
  gate, workflow hardening (permissions + SHA pinning).
- **Security docs:** `SECURITY.md` (+ enable PVR), `THREAT-MODEL.md`; BLE-input
  fuzz hardening and a no-`BinaryFormatter` confirmation.
- **User docs pivot** to download-and-run, including the honest verify/SmartScreen
  section; and the **foundation-doc amendments** (constitution packaging row,
  vision distribution criterion) in this PR.
- **Disposition of the Phase-5 MSIX/Store work** and closing the superseded issues.
- Cutting the **1.0 release** and updating roadmap/architecture.

### Out of scope

- **Any new device feature.** Pairing, battery/auto play-pause, codec
  transparency, mic-profile policy, noise-control, gesture remap, model/firmware
  coverage, and diagnostics (Phases 1–8) are **packaged and shipped as-is**, not
  changed here.
- **MSIX / Microsoft Store distribution** as a 1.0 channel (superseded; may
  return post-1.0 as an optional secondary channel — not planned here).
- The **Tier-2 KMDF driver** and its separate installer/signing — unchanged and
  still an explicit opt-in (Phase 6); the driver is never in the exe.
- **Instant SmartScreen trust** — impossible without the Store (excluded); we set
  honest expectations and build reputation. See Risks.
- A hosted docs website, telemetry, or any network call beyond the existing
  explicit update check (constitution: local-only).
- Winget/`winget` distribution (was tied to the Store `msstore` source).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  — **with the packaging row amended by this spec** to: self-contained single-file
  `.exe` (`PublishSingleFile` + `SelfContained` per-RID) via GitHub Releases; no
  MSIX/Store/installer for Tier 1 (the Tier-2 driver INF/`pnputil` note is kept).
  `Core` stays OS-free; adapters in `PodBridge.Windows`; composition root in
  `PodBridge.App`; warnings-as-errors in Core; max 50-line functions; **no bundled
  paid/proprietary component** (SignPath/Azure signing are external services, not
  bundled code).
- **No admin at run.** The exe keeps the `asInvoker` manifest; the HKCU Run key and
  `%LOCALAPPDATA%` storage are per-user and elevation-free.
- **Verify must not regress.** Publish props are gated behind
  `Condition="'$(RuntimeIdentifier)' != ''"`; `build/verify.ps1`
  (build slnx + `dotnet format --verify-no-changes` + `dotnet test`) stays the
  per-iteration gate and stays green. Publishing happens only in the release
  workflow.
- **Never trim** (`PublishTrimmed` unset) — SDK-disabled for WPF, errors
  NETSDK1168 (`docs/research/release-1.0.md` §1).
- **Honest surface** (constitution + vision): About/docs/release-notes state the
  AAC-vs-SBC reality, the A2DP↔HFP mic trade-off, and the expected first-run
  SmartScreen warning; never claim Apple-parity sound and never instruct a blanket
  SmartScreen bypass.
- **Local-only** (constitution): the security tooling (CodeQL, attestation, SBOM,
  Dependabot) runs in CI/GitHub, not in the shipped app; the app makes no new
  network call.
- **Research already done.** `docs/research/release-1.0.md` is the content
  authority for the publish, auto-start, and supply-chain work — implementers read
  it instead of re-running WebSearch (workflow-contract research-intensive rule
  satisfied at planning time). The signing step additionally confirms the chosen
  provider's current CI setup at implementation time (external, changeable).
- Architecture boundaries per `docs/architecture.md`: the portable auto-start is a
  new `PodBridge.Windows` adapter behind the existing Core `IStartupToggle`; the
  embedded-notices change is in `PodBridge.App`; `architecture.md` is updated to
  drop the MSIX packaging component and repoint the auto-start reference.

## Prior art

- [Legal & licensing (not legal advice)](../prior-art.md#legal--licensing-not-legal-advice)
  — Apache-2.0 clean-room, the coined name + "for AirPods" descriptor + not-
  affiliated disclaimer (unchanged by the distribution pivot); and the honest
  cost reality of signing (the EV-cert/Partner-Center friction it flags is exactly
  what the self-contained-exe + free-provenance direction sidesteps for Tier 1).
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — C#/.NET is the best Windows packaging story; AirPodsDesktop ships a plain
  installer/portable build (NSIS), precedent that a companion tool need not use
  MSIX/Store; MSIX cannot bundle the kernel driver anyway (that stays Phase 6).
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — winpods/AirPodsDesktop are distributed as direct downloads (not Store-only),
  precedent for a download-and-run companion; MagicPods' Store route is the path
  we are deliberately **not** taking for 1.0.
- [Windows audio codec & quality](../prior-art.md#windows-audio-codec--quality)
  — the honest AAC-vs-SBC wording the About surface, user docs, and release notes
  must carry (native AAC ceiling; never promise Apple-parity sound).
- Planning-time research consolidated in **`docs/research/release-1.0.md`**
  (single-file publish, SmartScreen/signing, GitHub supply-chain baseline,
  portable auto-start, security docs/BLE hardening) — the implementation content
  authority.

## Human prerequisites

Delivered or confirmed by the human at the spec-acceptance gate so the implement
loop runs without interruption. Several are **conditional on the open decisions**.

- [ ] **Signing = free + provenance (decided).** Submit the **SignPath Foundation**
      OSS Request Form (free, no residency limit) and budget for vetting turnaround
      — SignPath requires the project to be **already released in the form to be
      signed**, so first signing may lag the very first tag. Until it lands, 1.0
      ships unsigned-with-provenance; the release-cut does **not** block on SignPath
      (attestation/checksums are the trust anchor), and a signed re-release follows
      once SignPath is approved.
- [ ] **Enable GitHub Private Vulnerability Reporting** (Settings → Security →
      Private vulnerability reporting → Enable) — one-time, free, owner/admin.
- [ ] **Confirm/enable the free GitHub security features** that need repo-admin:
      CodeQL code scanning, Dependabot (version + security), dependency review,
      **secret scanning + push protection**. (For a public repo these are free; the
      workflows/config are added by the implementation, but the admin toggles are
      the human's.)
- [ ] Access to **Windows-on-ARM hardware** (or an arm64 VM) to smoke-test the
      arm64 asset at the human-QA gate — the build cross-compiles arm64 from the x64
      runner, but behavioural QA of the arm64 exe needs arm64 hardware.
- [ ] **(Recommended) Branch protection on `main`** requiring Verify + CodeQL +
      dependency review to pass and restricting force-push — so the hardened
      supply chain is actually enforced. Owner/admin action.
- [ ] **Decide the `SECURITY.md` triage channel wording** (GitHub-PVR-only vs an
      email contact) and who monitors reports — a project-policy choice.
- [ ] **Confirm the Microsoft Partner Center account is no longer needed** for 1.0
      and can be dropped (Store is out of scope).

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| **Signing = free + provenance** (resolved at the gate): ship unsigned but backed by build-provenance attestation + `checksums.sha256` + SBOM, and apply to **SignPath Foundation** for free OSS OV-equivalent signing (no residency limit). Self-signed and EV ruled out; Azure/OV declined | No option except the excluded Store removes the first-run warning; the free provenance baseline is the trust anchor and SignPath adds a verified publisher at $0. `docs/research/release-1.0.md` §2 | 2026-07-11 |
| **MSIX/Store = archive & supersede** (resolved at the gate): mark Phase 5 superseded, archive its spec, remove the packaging project/workflow/manifest/msstore artifacts + the dead `StartupTaskToggle`, close #38 as superseded | Preserves git history, stops CI waste, keeps the reusable About/auto-start/license pieces; "rip out" premature, "keep dormant" rots the roadmap. §Risks | 2026-07-11 |
| **Target arch = `win-x64` + `win-arm64`** (resolved at the gate) | arm64 is ~a second publish line (crossgen2 from the x64 runner) and covers Windows-on-ARM natively; the extra QA asset is accepted (needs arm64 hardware at the QA gate). `docs/research/release-1.0.md` §1 | 2026-07-11 |
| Distribution = **self-contained single-file `.exe` via GitHub Releases**; no MSIX/Store/installer for Tier 1 | The owner's explicit 1.0 direction (download-and-run, no install); unblocks the release from the paid Partner Center dependency that stalled Phase-5 #38 | 2026-07-11 |
| Publish props **scoped behind `Condition="'$(RuntimeIdentifier)' != ''"`** | Keeps the plain `dotnet build`/Verify path (no RID) completely unaffected — Verify must not regress | 2026-07-11 |
| **Never** set `PublishTrimmed` | SDK-disabled for WPF; errors NETSDK1168 and produces a broken app — settled constraint, not a choice | 2026-07-11 |
| R2R **on**; `EnableCompressionInSingleFile` **default OFF**, enabled only if measurement shows it cuts download size materially (≥ ~20 MB) **without** a cold-start regression beyond ~300 ms; the measured result is recorded in the release notes | Microsoft: the compression trade-off is app-specific — measure, don't assume; the tie-breaker keeps two implementers from diverging and makes "measured and recorded" deterministic | 2026-07-11 |
| The **three loose payload files** (`LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`) become **embedded resources**, not `Content` sidecars; About reads the embedded copy (keep `FallbackNotices`) | A single-file exe must be sidecar-free, and a standalone-downloaded exe must still carry its Apache-2.0 license text + NOTICE attribution (Apache-2.0 §4) | 2026-07-11 |
| Auto-start = per-user **`HKCU\…\Run`** (quoted `Environment.ProcessPath`), default OFF, path rewrite-on-enable + self-heal-on-launch; **read `StartupApproved\Run` to honour a user disable** (`DisabledByUser`) | Needs no admin, only `Microsoft.Win32.Registry` (single-file-safe, no COM/`.lnk` dep); the value *is* the command line; preserves the existing `StartupToggleState` contract. `docs/research/release-1.0.md` §4 | 2026-07-11 |
| Free GitHub-native supply-chain baseline (attestation + `checksums.sha256` + SBOM + CodeQL + Dependabot + dependency review + **secret scanning + push protection** + hardened/pinned workflows) is the trust backbone, independent of the signing decision | All free for a public repo; attestation fails closed (stronger than a same-page checksum); secret scanning + push protection back the constitution's "no secrets committed" Don't; lets signing stay a separate optional spend | 2026-07-11 |
| Reproducible build: `Deterministic=true` (default), `ContinuousIntegrationBuild=true` **CI-only**, SourceLink/`DotNet.ReproducibleBuilds`, commit-mapped PDBs | Makes the release binary verifiable (auditable-OSS positioning); CI-only avoids baking CI paths into local-debug PDBs | 2026-07-11 |
| `SECURITY.md` + GitHub Private Vulnerability Reporting as the sole report channel; lightweight GHSA/CVE-on-request stance | Free, ~5 min, the mainstream 2026 solo-maintainer OSS pattern; right-sized for pre-adoption scale | 2026-07-11 |
| BLE bytes are untrusted input; keep/extend the bounds-checked `ContinuityParser` + fuzz tests; no `BinaryFormatter` anywhere | CVE-2023-24871 (OOB write in Windows' own BLE parser) is the precedent; hardening existing coverage, not a rewrite | 2026-07-11 |
| This spec's PR **amends `docs/constitution.md` (packaging row) and `docs/vision.md` (distribution criterion)** to match the pivot | They are permanently loaded and binding; leaving them contradicting the accepted direction would mislead every future context and implementer | 2026-07-11 |

## Tracking

The decomposition into steps lives as GitHub issues, one per implementable step
grouped under this phase's milestone. This spec owns the design; the issues own
progress. Do not duplicate the step list here.

- Milestone: created on merge (one for this phase); `Depends on milestone: none`
  (all feature milestones #1–#8 are closed; #5's distribution work is superseded
  by this phase, not a blocking dependency).
- Issues: created from this spec once merged (one per implementable step), each
  referencing this spec path and, where relevant, `docs/research/release-1.0.md`.

## Verification

This list doubles as the human milestone-QA gate script (the Test command covers
only device-independent Core/adapter logic; behavioural/desktop items are checked
by the human on real hardware, per `docs/workflow.md`). A QA manual
(`docs/qa/release-1.0-*.md`) is written before the QA gate.

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format, unit tests all green — **and the plain build is unchanged** (the
      scoped publish props do not affect it).
- [ ] **CI on `windows-latest`** publishes the self-contained single-file exe for
      each selected RID; the exe is present as a workflow artifact / release asset.
- [ ] **`gh attestation verify <exe> -R bhemsen/PodBridge` succeeds** for the
      released exe, and **fails** for a tampered copy (fails-closed check).
- [ ] `checksums.sha256` matches each asset (`certutil -hashfile <exe> SHA256`),
      and an **SBOM** is attached to the release.
- [ ] The release exe is **signed per the accepted strategy** (verify the
      Authenticode publisher) **or** deliberately unsigned-with-provenance per that
      decision — **never self-signed**.
- [ ] **CodeQL (C#)** runs on push/PR; **Dependabot** config is active for `nuget`
      + `github-actions`; **secret scanning + push protection** are enabled; a
      **dependency-review/vulnerable-package gate** runs on PRs; **every workflow**
      has a least-privilege `permissions:` block and pins third-party actions to
      **commit SHAs**.
- [ ] `SECURITY.md` exists (supported versions + PVR), **PVR is enabled**, and
      `THREAT-MODEL.md` covers the documented surfaces.
- [ ] A **device-independent unit test** drives the new HKCU auto-start adapter
      (or its testable seam) — enable → Run value written with the quoted current
      path; disable → cleared; default **OFF**; stale-path → self-healed; a
      Task-Manager `DisabledByUser` state is reported, not overridden.
- [ ] Extended `ContinuityParser` **fuzz tests** assert no OOB read on malformed
      length/count fields; a review confirms **no `BinaryFormatter`** on local files.
- [ ] The **branding/disclaimer invariant** unit test still passes with notices
      embedded (name contains neither "Apple" nor "AirPods"; disclaimer present;
      license Apache-2.0).
- [ ] **(Human QA — clean Windows 11 + real AirPods):** download the exe, run it
      with **no admin and no install**; expected first-run SmartScreen warning is
      shown and the "Verify your download" steps work; tray reaches
      battery-visible in **≤ 2 minutes**; About shows the disclaimer + embedded
      license/notices + version when the exe is run **from an otherwise-empty
      folder** (no file beside the exe); the auto-start toggle
      takes effect and **persists across a reboot** (default OFF), and still fires
      after the exe folder is **moved-and-relaunched** (self-heal); settings/logs
      appear under `%LOCALAPPDATA%\PodBridge`; the **Tier-1 suite passes with no
      driver present** and the app runs `asInvoker` (no elevation prompt).
- [ ] The **1.0 release** (tag `v1.0.0`) exists with honest notes (driver-free
      Tier-1 scope, SmartScreen expectation, verification steps, measured exe
      size); `docs/roadmap.md`/`docs/architecture.md` reflect the shipped state;
      the Phase-5 MSIX work is dispositioned and **#38** is closed as superseded.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| First-run SmartScreen "Unknown publisher"/"Windows protected your PC" undercuts the ≤2-min low-friction goal; **no** option except the excluded Store removes it instantly | Set expectations honestly in release notes with a verify-then-run walkthrough (checksum + `gh attestation verify`); pursue free SignPath signing (verified publisher name) and **reuse one signing identity across releases** so reputation carries forward; never instruct a blanket bypass |
| **Smart App Control** / enterprise policy can block an **unsigned** exe outright | Strongest argument for a publicly-trusted signature (SignPath free, or Azure ~$120/yr); document the locked-down-environment limitation; attestation/checksums remain the integrity fallback |
| Self-contained single-file WPF exe can be large (tens of MB → ~150 MB) | Measure PodBridge's real output on first publish; evaluate compression vs startup; publish per-RID so users download only their arch; state the size in release notes |
| Accidentally enabling `PublishTrimmed` breaks the WPF app (NETSDK1168) | Explicitly never set it; a csproj comment + this spec record it; use compression (not trimming) for any size reduction |
| Portable auto-start stale-path: user moves the folder and never relaunches → Run entry silently doesn't fire (no OS warning) | Rewrite the value on every enable + self-heal on each launch while Enabled (compare to `Environment.ProcessPath`); document the moved-and-never-relaunched edge as a known limitation |
| Single-file native-lib self-extract to `%TEMP%\.net` could be tampered by another local user if the dir is world-writable (MS security note) | Verify the default extraction dir is per-user; document `DOTNET_BUNDLE_EXTRACT_BASE_DIR` and cover local-tampering in the threat model |
| Superseding merged Phase-5 MSIX/Store work orphans 4 reviewed PRs and leaves `packaging.yml` burning CI minutes on a dead channel; About/auto-start were coupled to MSIX identity | Archive-and-supersede (default), not delete: disable `packaging.yml`, mark Phase 5 superseded, re-home the packaging-independent About/disclaimer/license-notices, and remove only the genuinely dead `StartupTaskToggle` alongside the new HKCU adapter |
| Untrusted BLE advertisement bytes are a real RCE-class surface (cf. CVE-2023-24871) | Keep the fixed-offset bounds-checked `ContinuityParser`, extend fuzz coverage for malformed length/count fields, document the untrusted-input boundary |
| `ContinuousIntegrationBuild` enabled locally would bake CI paths into PDBs and break local debugging | Pass it **only** in the CI release command; keep `Deterministic=true` everywhere; verify reproducibility from CI PDBs only |
| Embedding notices wrong → About shows nothing / stale text | Unit-test the branding/notices invariant against the embedded resource; keep the existing hard-coded fallback string |

## Decision log

- 2026-07-11: Spec drafted from a planning-time multi-source research pass
  (5 parallel researchers + Opus synthesis), consolidated in
  `docs/research/release-1.0.md`. The direction is a deliberate owner pivot from
  Phase 5's MSIX + Microsoft Store mechanism (blocked on a paid Partner Center
  account, issue #38) to a self-contained single-file `.exe` via GitHub Releases.
  Settled by constraint/precedent: scoped publish props (Verify unaffected),
  never-trim, embedded notices, HKCU auto-start, the free supply-chain baseline,
  reproducible build, `SECURITY.md`/PVR, and BLE-input hardening of existing
  coverage. Three genuinely-open points carried to the spec-acceptance gate:
  signing strategy, MSIX/Store disposition, and target architecture. The PR also
  amends the constitution (packaging row) and vision (distribution criterion),
  ratified at the gate. Implementer pre-mortem surfaced the embedded-notices
  requirement (a single file must be sidecar-free) and the scoped-condition guard;
  both baked in above.
- 2026-07-11: **Spec-acceptance gate resolved** (AskUserQuestion). Signing = free +
  provenance (ship unsigned with attestation/checksums/SBOM; apply to SignPath
  Foundation). MSIX/Store = archive & supersede. Target architecture =
  `win-x64` + `win-arm64`. All three matched the recommended defaults; baked into
  Prior decisions and the affected Outcome/prerequisite items above. Constitution
  packaging row + vision distribution criterion amended in this PR are accepted
  together with the spec.
