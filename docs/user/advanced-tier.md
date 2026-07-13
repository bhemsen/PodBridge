# PodBridge advanced tier (optional) — noise-control switching

> **PodBridge** is not affiliated with, authorized, sponsored, or endorsed by
> Apple Inc. "AirPods" and "Apple" are trademarks of Apple Inc., used here only
> descriptively. PodBridge is open-source software licensed under **Apache-2.0**.

The **advanced tier** adds **noise-control switching** — **Off / Noise
Cancellation / Transparency / Adaptive** — from the tray, on supported AirPods
(reference model: **AirPods Pro 2 (USB-C)**). Everything in the default
**PodBridge** experience — battery, automatic play/pause, audio guidance, the
microphone-profile policy — is **Tier 1** and needs **no driver and no admin**.
The advanced tier is a separate, opt-in add-on and is **not required** for any of
that.

**This is an advanced, invasive, opt-in step.** It installs a small kernel
driver and requires you to lower your machine's driver-security bar. Read the
honesty section below before deciding. If in doubt, you do not need it.

---

## Why a driver, and why it is not "just signed"

Windows only lets a **kernel driver** open the Bluetooth-Classic L2CAP channel
(PSM `0x1001`) that AirPods use for noise-control commands — user-mode apps
cannot. So the advanced tier ships a tiny **KMDF L2CAP bridge driver**
(`driver/PodBridgeAAP`) that PodBridge talks to.

A kernel driver on 64-bit Windows must be **signed and trusted** to load. A
Microsoft-signed release would need an **EV code-signing certificate**
(~$250–560/year) and a **Microsoft Partner Center** account — a paid, human
prerequisite. PodBridge is a free, open-source project, so this tier ships
**TEST-signed** with a **self-signed certificate** (no purchase, no account) —
either the project's stable test certificate in the **pre-built release**, or one
you generate locally if you build the driver yourself.

> **PodBridge makes no claim of a Microsoft-signed or production-attested
> driver.** The attestation path (EV certificate + Partner Center) is documented
> here only as a **deferred, out-of-scope** option for a possible future signed
> release. It is not part of this build.

---

## The honesty section — TWO machine-wide security changes

To load a **test-signed** driver on 64-bit Windows, **both** of the following are
required. Neither alone is enough, and **both are machine-wide security
changes**:

1. **Enable test-signing mode.** Windows must be told to load test-signed
   drivers:

   ```
   bcdedit /set testsigning on
   ```

   then **reboot**. This is a machine-wide setting. **You must do this
   yourself** — PodBridge and its installer **never** run `bcdedit` on your
   behalf (it is on the project's command deny-list, precisely because it is a
   security-relevant, machine-wide change).

2. **Trust the self-signed test certificate.** Even with test-signing on, x64
   rejects a driver whose publisher it does not trust. The installer imports the
   certificate into **both** machine certificate stores — **Trusted Root
   Certification Authorities** and **Trusted Publishers**. (Skipping the root
   import gives the classic "a certificate chain … terminated in a root
   certificate which is not trusted" load failure.)

**The security trade-off, plainly:** test-signing mode lets *any* test-signed
driver load, and trusting the self-signed certificate means *anything signed with
it* is treated as a trusted publisher on your machine. Together they lower your
machine's driver-security bar until you undo them. That is why the whole tier is
**strictly opt-in**, both changes are **reversible**, and **every default (Tier-1)
feature keeps working without either of them.**

The installer performs **only** the certificate trust (step 2), inside the one
explicit elevated step. Step 1 (`bcdedit`) stays your manual action.

---

## Install (the opt-in, elevated flow)

You can start this from inside PodBridge (tray → **Noise control** → **Enable
advanced tier…**), which shows the same warning and then launches the elevated
installer for you. Or do it by hand:

### 1. Get the driver package

**Option A — download the pre-built release (recommended).** The driver is
published as its **own** GitHub release, separate from the app. On the
[**Releases page**](https://github.com/bhemsen/PodBridge/releases) pick the latest
**`driver-v…`** release and download `PodBridgeAAP-driver-<version>-x64.zip`.

The package is **test-signed with the project's stable test certificate** and its
build provenance is attested. Verify your download before trusting it:

```powershell
gh attestation verify PodBridgeAAP-driver-<version>-x64.zip --repo bhemsen/PodBridge
# and/or check it against the release's checksums.sha256
```

Extract the zip. It contains, side by side: `install-advanced-tier.ps1`,
`PodBridgeAAP.inf`, `PodBridgeAAP.sys`, `PodBridgeAAP.cat`, and the public
`PodBridgeTest.cer`.

- To use the in-app **Enable advanced tier…** button to run the installer for you,
  extract into `%LocalAppData%\PodBridge\advanced-tier\` (the app looks there, and
  in `PODBRIDGE_ADVANCED_TIER_DIR` and next to the app).
- Otherwise just run the installer from wherever you extracted it (step 2), or pass
  `-PackageDir <extracted-folder>`.

**Option B — build (and test-sign) it yourself.** From the repository, in a normal
shell (needs Visual Studio, or the **Build Tools**, with the **Desktop development
with C++** workload + the **Windows Driver Kit** component; the WDK build files are
restored from NuGet):

```powershell
cd driver/PodBridgeAAP
.\build-testsign.ps1
```

This produces a test-signed package (`PodBridgeAAP.sys` / `.inf` / `.cat`) and a
locally-generated self-signed `PodBridgeTest.cer` under
`driver/PodBridgeAAP/x64/Release`. It does **not** install anything or change any
security setting.

### 2. Run the install step (elevated; installs the driver + trusts the cert)

```powershell
.\install-advanced-tier.ps1 -Action install
```

The script **self-elevates** (a single Windows admin prompt). In that one elevated
step it:

- imports `PodBridgeTest.cer` into `LocalMachine\Root` **and**
  `LocalMachine\TrustedPublisher` (the equivalent of
  `CertMgr.exe /add PodBridgeTest.cer /s /r localMachine root` and
  `… trustedpublisher`); then
- runs `pnputil /add-driver PodBridgeAAP.inf /install`.

It does **not** run `bcdedit` and it does **not** enable test-signing.

### 3. Enable test-signing mode yourself, then reboot

From an **elevated** prompt (this is the one step PodBridge never does for you):

```powershell
bcdedit /set testsigning on
```

Then **reboot**. After the reboot the test-signed driver can load. Relaunch
PodBridge — it re-checks for the driver on startup and enables the **Noise
control** submenu when the driver is present.

> While test-signing mode is on, Windows shows a "Test Mode" watermark on the
> desktop. That is expected.

---

## Using it

With the driver installed (test-signing on **and** the certificate trusted) and
supported AirPods connected, the tray **Noise control** submenu lets you pick
**Off / Noise Cancellation / Transparency / Adaptive**. A change is applied
optimistically and confirmed by the AirPods' echo; if no echo arrives it reverts
with a short "couldn't confirm" notice. **Adaptive** is offered only on models
that report support (Pro 2 reference model).

If the driver is not installed, the submenu is disabled with an honest
explanation and the **Enable advanced tier…** affordance — never silently broken.

---

## Uninstall (reverse both changes)

```powershell
cd driver/PodBridgeAAP
.\install-advanced-tier.ps1 -Action uninstall
```

Elevated, this removes the driver (`pnputil /delete-driver <oemNN.inf>
/uninstall`) and removes the test certificate from both machine stores. To also
turn off test-signing mode (recommended once you are done):

```powershell
bcdedit /set testsigning off   # elevated, then reboot
```

Uninstalling the advanced tier does not affect Tier-1 PodBridge at all.

---

## What this tier is **not**

- **Not** a Microsoft-signed / production-attested driver. It is test-signed with
  a self-signed certificate; loading it requires the two opt-in machine-wide
  changes above.
- **Not** installed by default, **not** bundled in the PodBridge app (MSIX), and
  **not** installed or elevated silently. The app always runs `asInvoker`; the
  only elevation is the install step you explicitly trigger.
- **Not** required for battery, play/pause, audio guidance, or the
  microphone-profile policy — those are Tier 1 and always driver-free.

---

## Deferred (out of scope): a Microsoft-signed release

A publicly distributable, **attestation-signed** driver that loads on stock
Windows with **no** test-signing and **no** certificate import would need an **EV
code-signing certificate** plus a **Microsoft Partner Center** account (submit the
package, receive a Microsoft-signed driver back). That is a paid, human-provisioned
prerequisite and is **out of scope** for this tier; it is recorded here only so it
can be pursued deliberately before any signed release. See
[`docs/specs/spec-advanced-driver-anc.md`](../specs/spec-advanced-driver-anc.md).
