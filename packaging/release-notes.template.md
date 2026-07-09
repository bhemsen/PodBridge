<!--
  GitHub Release notes template for the self-signed manual-fallback MSIX channel
  (issue #36). The Package & Release (MSIX) workflow reads this file on a tag
  build and replaces the __TAG__ token with the release tag (e.g. v0.1.0).
-->
## PodBridge __TAG__ — driver-free Tier-1 (self-signed manual fallback)

This asset is the **self-signed GitHub-Releases MSIX**, a *manual fallback*
channel. The primary **no-admin** install path is the Microsoft Store via
`winget install <StoreProductId> -s msstore` and goes live after Store
certification (see issue #38); it needs no certificate step.

### Install this fallback MSIX (one-time trust step, requires admin once)

The MSIX is signed with a throwaway self-signed certificate that Windows does
not trust by default. In an **elevated** PowerShell, in the folder with the
downloaded assets, import the bundled `PodBridge-SelfSigned.cer` once, then
install the package (the install itself needs no admin):

```powershell
Import-Certificate -FilePath .\PodBridge-SelfSigned.cer `
  -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\PodBridge-__TAG__.msix
```

Full details, including how to undo trust: `packaging/README.md`.

### Scope & honesty (Tier-1, driver-free)

- Battery, automatic play/pause, codec transparency and the microphone-profile
  policy — **no admin, no driver** at run time (`asInvoker`).
- Audio is **not** Apple-identical: media uses AAC where the hardware supports
  it, otherwise SBC; using the AirPods microphone forces the Bluetooth
  A2DP↔HFP trade-off (call-quality audio) — a platform limit, not a bug.
- Advanced features (noise-control switching, gesture remap) are a later,
  opt-in add-on and are **not** in this release.

Not affiliated with, authorized, or endorsed by Apple Inc.
