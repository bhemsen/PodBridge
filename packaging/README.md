# Packaging & distribution

How PodBridge is packaged into an MSIX and shipped. This is developer/release
documentation; the end-user quickstart lives in the user guide (issue #37).

PodBridge ships as an **app-only MSIX** (no kernel driver — the Tier-2 driver is
a separate Phase-6 installer). The MSIX is built from the Windows Application
Packaging Project in [`PodBridge.Package/`](PodBridge.Package/) by the
**`Package & Release (MSIX)`** workflow (`.github/workflows/packaging.yml`) on
`windows-latest`. See [`docs/research/msix-packaging.md`](../docs/research/msix-packaging.md)
for the packaging/signing facts this is built on.

> **The optional advanced-tier driver is never in this MSIX.** MSIX cannot
> cleanly carry a kernel driver, and the driver is a strictly opt-in, separately
> installed add-on. It builds with the WDK (`driver/PodBridgeAAP/build-testsign.ps1`)
> and installs via its own elevated flow
> (`driver/PodBridgeAAP/install-advanced-tier.ps1`: `pnputil` + self-signed
> test-cert trust). End-user docs: [`docs/user/advanced-tier.md`](../docs/user/advanced-tier.md);
> design: [`docs/specs/spec-advanced-driver-anc.md`](../docs/specs/spec-advanced-driver-anc.md).

## Two distribution channels

| Channel | Signing | Admin to install? | When live |
|---|---|---|---|
| **Microsoft Store** (primary, no-admin) | Microsoft re-signs for free; Store trust is pre-installed on Windows 11 | **No** — per-user install, no certificate step | After Store certification (**#38**) |
| **GitHub Releases** (manual fallback) | Self-signed throwaway cert generated in CI | **Once** — an admin must trust the bundled `.cer` first | Now (this issue, **#36**) |

Only the Store channel is the **no-admin** path. The self-signed GitHub-Releases
MSIX is a **documented manual fallback**: because its signer is not trusted by
default, the certificate must be imported once from an elevated session before
the package will install (below).

## Release workflow (self-signed GitHub-Releases fallback)

Pushing a semver tag `vMAJOR.MINOR.PATCH` (e.g. `v0.1.0`) runs the packaging
workflow, which — reusing the exact same build + sign steps as every branch
build — additionally:

1. stamps the MSIX `<Identity Version>` to `MAJOR.MINOR.PATCH.0` from the tag;
2. builds the `.wapproj` MSIX and signs it with an in-workflow **self-signed**
   certificate whose Subject equals the manifest `Publisher`
   (`CN=PodBridge (Self-Signed CI)`) — no signing secret is needed;
3. creates a **GitHub Release** for the tag and attaches both the signed
   **`PodBridge-<tag>.msix`** and the matching **`PodBridge-SelfSigned.cer`**
   trust certificate.

Branch/PR builds are unchanged: they still publish the signed MSIX + cert as a
workflow **artifact** (they do not create a Release).

### One-time certificate-trust step (manual fallback only)

The self-signed signer is deliberately **not** trusted by Windows, so the MSIX
will not install until its certificate is trusted. This is a one-time,
**admin-only** step; it is the reason this channel is a fallback and **not** the
no-admin path. From an **elevated** PowerShell, in the folder with the
downloaded Release assets:

```powershell
# 1. Trust the release's signing certificate (machine-wide, admin — once).
Import-Certificate -FilePath .\PodBridge-SelfSigned.cer `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. Install the package (this part needs no admin).
Add-AppxPackage -Path .\PodBridge-<tag>.msix
```

Notes:

- App Installer checks the **machine** `TrustedPeople` store, not the per-user
  one — hence `Cert:\LocalMachine\TrustedPeople` and the admin requirement.
- The bundled `.cer` is the **public** certificate only (no private key); it is
  sufficient to trust the signer. Each release ships the `.cer` that matches
  that release's MSIX.
- To undo trust later: remove the certificate from
  `Cert:\LocalMachine\TrustedPeople` (admin).
- The app itself always runs **`asInvoker`** (no elevation at run time); admin
  is needed only for this one-time cert-trust step, never for the Store channel.

## Microsoft Store / `msstore` prep (identity is Partner-Center-derived)

The Store channel is **prepared** here but goes live only after certification
(#38). Its inputs come from reserving the coined product name in **Microsoft
Partner Center** — this phase does not invent them:

- [`msstore/msstore-entry.yaml`](msstore/msstore-entry.yaml) — the winget
  `msstore` entry: the Store **product ID** (the `msstore` `PackageIdentifier`,
  a 12-character alphanumeric) and the install command
  `winget install <StoreProductId> -s msstore --scope user`. Recorded against a
  `STORE_PRODUCT_ID_TBD` placeholder; there is **no** winget-pkgs community
  manifest for a Store app (the ID is resolved directly against `msstore`).
- [`msstore/store-association.template.xml`](msstore/store-association.template.xml)
  — the `Package.StoreAssociation.xml` template (VS "Associate App with the
  Store") with placeholders for the `Publisher`, `PublisherDisplayName`, and
  `MainPackageIdentityName`.

### Where the Store identity comes from

Reserving the name in Partner Center yields, in one place, every identity value
the Store channel consumes:

- the **Store product ID** — the `msstore` `PackageIdentifier` (`msstore-entry.yaml`);
- the MSIX **`Identity/Name`** — the Store-channel `<Identity Name>`
  (`MainPackageIdentityName` in the association template);
- the assigned **`Publisher`** (`CN=…`) and **publisher display name** — the
  `<Identity Publisher>` the Store re-sign supplies (so the local manifest only
  has to match the reserved value).

At Store submission (#38), these replace the placeholders and the throwaway
self-signed identity in
[`PodBridge.Package/Package.appxmanifest`](PodBridge.Package/Package.appxmanifest).

## Explicitly out of scope here (deferred to #38, after Store certification)

- **Submitting/certifying the Store listing.** Requires the Partner Center
  account (a human prerequisite) and Microsoft's certification.
- **Verifying the no-admin `winget install … -s msstore`.** The `msstore`
  listing does not resolve until the submission is certified/published, so the
  headline no-admin install is first exercised at the human-QA gate in #38 — it
  cannot be delivered or verified in this issue.
