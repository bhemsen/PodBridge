# Spec: Packaging & Distribution (Phase 5)

> Created: 2026-07-09

Wrap the finished driver-free feature set (Phases 1–4: pairing, battery + auto
play/pause, codec transparency, microphone-profile policy) into a distributable
product: a signed MSIX installable with **no admin rights**, a winget entry, the
first-ever user-facing window (an **About** surface carrying the mandatory
not-affiliated disclaimer, Apache-2.0 notices and honest audio wording), an
opt-in **auto-start-at-login** option, user documentation, and the Apache-2.0
packaging housekeeping — then cut the **first driver-free MVP release** that
concludes the Tier-1 MVP. This spec adds **no new device features**; it packages
and ships what Phases 2–4 built. This spec carries no lifecycle state —
acceptance is the spec merged on the default branch with a milestone and issues,
and all progress lives in the GitHub issues and milestone. A completed spec is
moved to `docs/specs/archive/`.

## Outcome

- [ ] `PodBridge.App` produces a **signed MSIX** via single-project MSIX
      packaging, built in **CI on `windows-latest`**, and **Verify stays green**.
- [ ] A **release workflow** builds/signs the MSIX and attaches it to **GitHub
      Releases** (self-signed manual channel), and the winget **`msstore` entry
      + Store-association manifest are prepared** against the Partner-Center-
      derived **Store product ID** (the `msstore` `PackageIdentifier`). The
      verified **no-admin `winget install`** from `msstore` is **first exercised
      at the human-QA gate after the Store submission is certified** — it cannot
      be delivered or verified before the Store listing exists (see Human
      prerequisites / MVP release).
- [ ] An **About window** is reachable from the tray menu and shows: the coined
      product name (**no "Apple"/"AirPods" in the name, no Apple logo**), the
      "for AirPods" descriptor, the **not-affiliated disclaimer**, the
      **Apache-2.0** license + third-party notices, the app version, an **honest
      audio/mic note** (never claims Apple-parity sound), and a link to the user
      docs.
- [ ] The **not-affiliated disclaimer + branding invariant** is covered by a
      **device-independent unit test**: product name contains neither "Apple" nor
      "AirPods" as the name, the disclaimer text is present, and the declared
      license is Apache-2.0 (constitution Tier-1 test gate).
- [ ] An **auto-start-at-login** option is implemented via the MSIX
      `windows.startupTask` extension, **default OFF**, toggled from the About
      surface; the toggle logic is covered by a **device-independent unit test**
      with a fake `IStartupToggle` (constitution Tier-1 test gate).
- [ ] Repo-root **`LICENSE` (Apache-2.0)**, **`NOTICE`**, and
      **`THIRD-PARTY-NOTICES.md`** exist, ship inside the package, and are
      surfaced in About; the tree is confirmed **clean-room** (no GPL source or
      verbatim doc prose).
- [ ] **User docs** (README quickstart + `docs/user/` guide) cover install
      (winget/Store), the **≤ 2-minute** fresh-install-to-battery-visible setup,
      the honest audio/mic caveats, the mic-profile policy modes, the auto-start
      toggle, uninstall, and the disclaimer.
- [ ] The **first driver-free MVP release** is cut: a tag, a GitHub Release with
      the signed MSIX and release notes stating the Tier-1 driver-free scope and
      honest limitations; the **Store submission is certified** and the no-admin
      `msstore` install path is verified at the human-QA gate;
      `docs/roadmap.md`/`docs/architecture.md` updated. The **Tier-1 test suite
      passes with no driver present** (graceful-degradation gate).
- [ ] The packaged app still runs **`asInvoker` (no elevation)** and installs and
      runs with **no admin and no driver** — the Tier-1 invariant re-verified at
      packaging time.

## Scope

### In scope

- Single-project MSIX packaging of `PodBridge.App` (package identity, manifest,
  logos/assets, minimal capabilities, `asInvoker`), producing a **signed** MSIX
  in CI. The Store-matching package identity (`Identity/Name` + `Publisher`) is
  applied from the Partner-Center reservation (see Human prerequisites); the CI
  artifact and GitHub-Releases fallback use a self-signed identity.
- **Distribution:** a winget entry (primary via the `msstore` source, backed by a
  Microsoft Store listing) plus a signed MSIX attached to GitHub Releases as a
  manual channel; a release workflow that builds, signs and publishes. The
  `msstore` entry/manifest and the Store association are **prepared here against
  the Partner-Center-derived Store product ID**; the actual Store submission,
  certification, and the first verified no-admin `msstore` install land in the
  **MVP-release** step (post-certification, at the human-QA gate).
- The **About window** (the app's first non-tray window) with the mandatory
  not-affiliated disclaimer, "for AirPods" descriptor, Apache-2.0 + third-party
  notices, version, honest audio wording, and a docs link — reachable from a tray
  "About" entry. (This is the disclaimer/About surface **deferred from Phase 1**.)
- **Auto-start-at-login** as an opt-in option via MSIX `windows.startupTask`
  (default OFF), toggled from About. (This is the auto-start option **deferred
  from Phase 1**.)
- **Apache-2.0 packaging:** `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`,
  SPDX/license metadata in the package, and a clean-room confirmation.
- **User documentation** (Markdown, in-repo).
- Cutting the **first driver-free MVP release** and updating the roadmap.

### Out of scope

- Any **new device feature.** Pairing/reconnect (**Phase 1**), battery + auto
  play/pause (**Phase 2**), codec transparency (**Phase 3**), and the
  microphone-profile policy (**Phase 4**) are packaged here, not built or changed.
- The **KMDF L2CAP driver**, its separate **INF/`pnputil` installer**, driver
  **signing / EV-cert / test-mode UX**, and ANC/Transparency/Adaptive switching —
  **Phase 6** (a driver is not part of the MSIX; constitution: MSIX cannot bundle
  a kernel driver cleanly).
- **Gesture remap** — **Phase 7**.
- Broadening supported models, firmware-fragility handling, and diagnostics —
  **Phase 8**.
- A hosted documentation website, telemetry, crash reporting, or any network call
  beyond the existing explicit update check (constitution: local-only).

## Constraints

- Stack, layering, license and quality principles per `docs/constitution.md`:
  **MSIX + winget** for the app, **Apache-2.0**, `Core` stays OS-free, adapters in
  `PodBridge.Windows`, composition root in `PodBridge.App`, warnings-as-errors in
  Core, max 50-line functions, **no bundled paid/proprietary component**.
- Component boundaries per `docs/architecture.md`: the auto-start capability is a
  new OS capability, so it sits behind a `Core` interface (`IStartupToggle`) with
  the `PodBridge.Windows` adapter (`StartupTaskToggle` over the MSIX
  `StartupTask` API); the About view-model lives in `PodBridge.App` and reads a
  disclaimer/branding constant from `Core`. `docs/architecture.md` is updated to
  list these when implemented (living doc).
- **No admin at install or run.** The packaged app keeps the `asInvoker`
  manifest. Because a sideloaded MSIX signed with a self-signed certificate needs
  a one-time machine-level certificate trust (which requires admin), the
  **no-admin install** success criterion is met through the **Store-signed**
  channel (Store trust is pre-installed); the self-signed GitHub-Releases MSIX is
  a documented manual fallback, not the no-admin path. The Store-signed channel —
  and therefore the no-admin criterion — is **only live once the Store submission
  is certified** (the MVP-release step); the `winget-distribution` step prepares
  the `msstore` entry but **cannot verify a no-admin install until the listing
  exists**, so that verification is deferred to the human-QA gate after
  certification.
- **Package identity is Partner-Center-derived.** The `msstore`
  `PackageIdentifier` (the Store product ID) and the Store-matching MSIX
  `Identity/Name` + `Publisher` do not exist until the human reserves the coined
  product name in Partner Center; they are inputs to the manifest, the `msstore`
  winget entry, and the Store submission, not values this phase invents (see
  Human prerequisites).
- **Honest audio surface** (constitution): About and docs state the AAC-vs-SBC
  reality and the A2DP↔HFP mic trade-off; no string claims Apple-parity sound.
- **Graceful degradation** (constitution): the MVP release ships with **no driver
  present**; the full Tier-1 test suite passes in that state and nothing in the
  UI implies a driver is required.
- Verify = `powershell -NoProfile -File build/verify.ps1`; CI must build the MSIX
  on `windows-latest`. Release signing runs in CI from a signing secret (see
  Human prerequisites), never a committed key.
- The MSIX packaging + signing/no-admin-install + `windows.startupTask` +
  winget/`msstore` manifest behaviour is **research-intensive** (≥ 3 Microsoft/
  winget source lookups) → split into a `chore:research-msix-packaging` issue
  whose research comment is the sole content authority for the implementation
  issues (workflow contract).

## Prior art

- [Legal & licensing (not legal advice)](../prior-art.md#legal--licensing-not-legal-advice)
  — Apache-2.0 clean-room, the coined name + "for AirPods" descriptor (no
  "AirPods"/"Apple" in the name, no Apple logo, not-affiliated disclaimer), and
  the honest-signing reality; the EV-cert/driver-signing burden it flags belongs
  to **Phase 6**, not this app-only MSIX.
- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — C#/.NET is the best Windows packaging story; **MSIX cannot bundle a kernel
  driver cleanly**, so the driver is a separate Phase-6 installer and this phase
  packages the app only.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — MagicPods ships through the **Microsoft Store** (Store id `9P6SKKFKSHKM`),
  precedent for the Store-signed, no-admin distribution channel and for the
  12-char Store-product-ID shape our `msstore` `PackageIdentifier` will take;
  winpods is the clean modern tray/distribution reference.
- [Windows audio codec & quality](../prior-art.md#windows-audio-codec--quality)
  — the honest AAC-vs-SBC wording the About surface and user docs must carry
  (native AAC is the ceiling; never promise Apple-parity sound).

## Human prerequisites

- [ ] **Microsoft Partner Center individual developer account** (one-time ~$19
      registration) — required to publish the Store listing that provides the
      **no-admin, Store-signed** install path, and the sole source of the
      package-identity values below. Delivered/confirmed at the spec-acceptance
      gate. The MSIX build, winget-manifest authoring, About surface, auto-start,
      licensing and docs proceed **without** the Store submission; the submission
      and certification themselves happen in the **MVP-release** step and are
      `blocked:human` until the account exists.
- [ ] **Store name reservation → concrete package identity (Partner-Center-derived
      outputs, delivered at the spec-acceptance gate).** Reserving the coined
      product name in Partner Center yields the identity values the rest of the
      phase consumes:
      - the **Store product ID** — the 12-char alphanumeric that is the `msstore`
        `PackageIdentifier` `winget install` resolves against (same shape as the
        MagicPods `9P6SKKFKSHKM` from prior art); it feeds the `msstore` winget
        entry;
      - the **MSIX package identity** — the `Identity/Name` and the assigned
        **`Publisher`** (and publisher display name) that the `Package.appxmanifest`
        and the Store-channel signing must match.
      These IDs do not exist until the human reserves the name; the self-signed
      GitHub-Releases MSIX uses its own throwaway identity and does not need them.
- [ ] **Code-signing secret in CI** — the certificate/PFX (or Store association
      signing) used to sign the release MSIX, provided as a GitHub Actions secret.
      For the manual GitHub-Releases MSIX a self-signed certificate is acceptable
      (documented trust step); the Store channel is signed by the Store.

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Apache-2.0 with root `LICENSE` + `NOTICE` + `THIRD-PARTY-NOTICES.md`, clean-room confirmed | Constitution mandates Apache-2.0 and forbids GPL source/verbatim prose; permissive + Store-friendly + explicit patent grant | 2026-07-09 |
| MSIX + winget as the distribution mechanism (app only; no driver in the package) | Constitution tech-stack row; prior-art confirms MSIX cannot cleanly bundle a kernel driver — that is the separate Phase-6 installer | 2026-07-09 |
| Coined product name with a "for AirPods" descriptor, no "Apple"/"AirPods" in the name, no Apple logo, a not-affiliated disclaimer in About | Constitution Don'ts + vision non-goals + prior-art legal note (3rd-party trademark guidelines) | 2026-07-09 |
| Packaged app keeps the `asInvoker` manifest — no elevation at run time | Constitution: Tier 1 needs no admin | 2026-07-09 |
| The `msstore` `PackageIdentifier` (Store product ID) and Store-matching MSIX `Identity/Name`+`Publisher` are Partner-Center-derived human outputs, not values this phase invents | The `msstore` source resolves against the Store product ID minted at name reservation; the MSIX identity must match the reserved Store identity — both exist only after Partner Center reservation (prior-art MagicPods Store id) | 2026-07-09 |
| The `winget-distribution` step delivers the release workflow + self-signed GitHub-Releases fallback + `msstore` association/manifest prep — **not** a verified `msstore` install; the first no-admin `msstore` install is exercised at the QA gate after Store certification (MVP-release step) | The `msstore` listing does not exist until the Store submission is certified, which is gated behind the Partner Center account and lands in the later MVP-release step per the issue DAG; `winget-distribution` runs before it and cannot deliver/verify the Store install | 2026-07-09 |
| The About/disclaimer surface and the auto-start option are delivered here, as explicitly deferred from Phase 1 | Phase 1 was tray-only; Phase-1 prior-decisions assigned both to Phase 5 (packaging) | 2026-07-09 |
| Honest audio/mic wording in About + docs; no Apple-parity claim | Constitution honest-audio-surface principle; prior-art AAC-vs-SBC note | 2026-07-09 |
| The MSIX/signing/startupTask/winget behaviour is research-intensive → `chore:research-msix-packaging` first, implementation issues depend on it and read its research comment | Workflow contract pre-classifies multi-source Windows-platform API confirmation as research-intensive | 2026-07-09 |
| DEFAULT — Primary distribution = **Microsoft Store** (Store-signed, no-admin) surfaced via winget `msstore`; a signed MSIX on GitHub Releases is the manual fallback | Only Store-signing (or a paid publicly-trusted cert) installs an MSIX with no admin; Store is free-to-user, matches prior-art (MagicPods Store id) and honours the hard no-admin success criterion. Recorded in openDefaults | 2026-07-09 |
| DEFAULT — **Single-project MSIX packaging** (`EnableMsixTooling` in `PodBridge.App`) rather than a separate `.wapproj` | `dotnet`/CI-friendly on `windows-latest` without Visual Studio-only project types; keeps packaging in the existing app project. Recorded in openDefaults | 2026-07-09 |
| DEFAULT — **Auto-start default OFF**, opt-in via an About toggle, implemented with MSIX `windows.startupTask` | Least-invasive default per vision; `windows.startupTask` is the MSIX-native, user-revocable (Task Manager) mechanism. Recorded in openDefaults | 2026-07-09 |
| DEFAULT — User docs are **in-repo Markdown** (README + `docs/user/`), no hosted site | Local-only ethos, zero infra/accounts, keeps the MVP lean. Recorded in openDefaults | 2026-07-09 |
| DEFAULT — The About surface is a **dedicated WPF About window** launched from the tray, not a full settings shell | Smallest surface that satisfies the disclaimer/notices/version/auto-start-toggle requirement; a full settings window is not in scope. Recorded in openDefaults | 2026-07-09 |

## Tracking

The decomposition into steps lives as GitHub issues, not in this file — one issue
per step, grouped under this phase's milestone. This spec owns the design; the
issues own progress. Do not duplicate the step list here.

- Milestone: created on merge (one per this phase); depends on the Phase-4
  milestone (wired outside this spec).
- Issues: created from this spec once merged (one per implementable step).

Each issue references this spec path in its body.

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and unit tests all green.
- [ ] The **`chore:research-msix-packaging` research comment** is posted (sources
      + consensus on single-project MSIX packaging for WPF/.NET 10, signing &
      no-admin install semantics, `windows.startupTask`, and the winget/`msstore`
      manifest) and its consensus is reflected in the packaging implementation
      (workflow contract: research comment as QA artefact).
- [ ] **CI on `windows-latest`** builds a **signed MSIX** and publishes it as a
      workflow artifact.
- [ ] A **device-independent unit test** asserts the branding/disclaimer
      invariant: product name contains neither "Apple" nor "AirPods" as the name,
      the not-affiliated disclaimer string is present, and the declared license is
      Apache-2.0 (Tier-1 test gate).
- [ ] A **device-independent unit test** drives the auto-start toggle through a
      fake `IStartupToggle` and asserts enable → startup-registered and disable →
      startup-cleared, with the **default OFF** (Tier-1 test gate).
- [ ] The **About window** opens from the tray "About" entry and shows the coined
      name, "for AirPods" descriptor, the not-affiliated disclaimer, Apache-2.0 +
      third-party notices, version, honest audio/mic note, and a docs link.
- [ ] Repo-root `LICENSE` (Apache-2.0), `NOTICE`, `THIRD-PARTY-NOTICES.md` exist,
      are included in the package payload, and a review confirms the tree is
      clean-room (no GPL source/prose).
- [ ] The **release workflow** builds + signs the MSIX and attaches it to a
      **GitHub Release** (self-signed manual channel), and the winget `msstore`
      entry + Store-association manifest are authored against the
      Partner-Center-derived Store product ID; the manual-MSIX certificate-trust
      step is documented. (No verified `msstore` install here — the Store listing
      does not exist yet; see the QA-gate item below.)
- [ ] User docs (README + `docs/user/`) document the winget/Store install, the
      ≤ 2-minute setup, honest audio/mic caveats, mic-policy modes, the auto-start
      toggle, uninstall, and the disclaimer.
- [ ] The **Store submission is certified** and release notes for the MVP tag
      state the **driver-free Tier-1 scope** and the honest limitations;
      `docs/roadmap.md`/`docs/architecture.md` are updated.
- [ ] The **Tier-1 test suite passes with no driver present** (graceful
      degradation), and the packaged app runs **`asInvoker`** (no admin prompt).
- [ ] **(Human QA gate — after Store certification)** On a clean Windows 11
      machine with real AirPods: `winget install` from the **`msstore` source**
      completes **with no admin prompt** (the headline no-admin path, first
      exercised here); the self-signed GitHub-Releases MSIX installs after the
      documented one-time trust step; app starts to the tray in ≤ 2 minutes to
      battery-visible; the About window shows the disclaimer; the auto-start
      toggle takes effect and **persists across a reboot** (default OFF);
      uninstall via winget/Settings is clean.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Self-signed sideloaded MSIX needs an admin cert-trust step, breaking the no-admin promise | Ship the **Store-signed** channel as the primary/no-admin path (winget `msstore`); treat the GitHub-Releases MSIX as a clearly-documented manual fallback with the trust step spelled out. |
| `winget-distribution` runs before the Store listing exists, so it cannot verify its headline no-admin `msstore` install | Rescoped: `winget-distribution` delivers only the release workflow + self-signed GitHub-Releases fallback + `msstore` association/manifest prep; the first verified no-admin `msstore` install is an MVP-release/QA-gate item, after Store certification. |
| The `msstore` `PackageIdentifier` / MSIX Store identity are unknown at implementation time | Pinned as Partner-Center-derived human outputs delivered at the spec-acceptance gate (Store product ID + `Identity/Name`+`Publisher`); the self-signed fallback uses a throwaway identity so non-Store work is never blocked on them. |
| Microsoft Partner Center account/fee blocks the release | Flagged as a human prerequisite; all non-Store work proceeds without it, and only the Store submission/certification (in the MVP-release step) is `blocked:human` until delivered. |
| Single-project MSIX tooling misbehaves for a WPF/.NET 10 app in headless CI | Confirmed up front by `chore:research-msix-packaging`; CI builds the package on every push so regressions surface immediately. |
| `windows.startupTask` behaves differently unpackaged vs packaged | Toggle sits behind `IStartupToggle` with a fake for unit tests; real behaviour verified at the human QA gate (persists across reboot). |
| Accidental GPL contamination when assembling third-party notices | `THIRD-PARTY-NOTICES.md` enumerates each dependency + license; a clean-room review is an explicit verification item. |
| Distribution channel decision is genuinely open | Documented default (Store-primary + GitHub-Releases fallback) recorded in Prior decisions and openDefaults; revisited at the spec-acceptance gate. |

## Decision log

- 2026-07-09: Spec drafted. Five genuinely-open points settled with documented
  defaults (distribution/signing channel; single-project MSIX vs `.wapproj`;
  auto-start default state/mechanism; docs form; dedicated About window) and
  recorded in openDefaults for the spec-acceptance gate. Constitution Tier-1 test
  gate honoured with two device-independent unit tests (branding/disclaimer
  invariant; auto-start toggle via fake). Human prerequisites: a Microsoft
  Partner Center account (Store submission) and a CI signing secret.
- 2026-07-09: Addressed the spec-review must-fix items. (1) Resolved the
  winget-`msstore` ↔ Store-submission sequencing fork: `winget-distribution`
  (which runs before `mvp-release` in the DAG) delivers only the release workflow
  + self-signed GitHub-Releases fallback + `msstore` association/manifest prep —
  not a verified `msstore` install; the headline no-admin `msstore` install is
  first exercised at the human-QA gate after Store certification in the
  MVP-release step. (2) Pinned the concrete package identity as
  Partner-Center-derived human outputs: the Store product ID (the `msstore`
  `PackageIdentifier`) and the MSIX `Identity/Name`+`Publisher`, delivered at the
  spec-acceptance gate, so no implementer is blocked on an unknown ID.