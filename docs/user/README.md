# PodBridge user guide

**PodBridge** is an open-source companion **for AirPods on Windows** — battery,
automatic play/pause, honest audio guidance and a microphone-profile policy, all
**driver-free and with no administrator rights**.

> PodBridge is not affiliated with, authorized, sponsored, or endorsed by Apple
> Inc. "AirPods" and "Apple" are trademarks of Apple Inc., used here only
> descriptively to identify the hardware this software works with. PodBridge uses
> no Apple logo.

This guide covers the **driver-free MVP** (Tier 1): install, the ≤ 2-minute
fresh-install-to-battery-visible setup, the honest audio and microphone caveats,
the microphone-profile modes, the start-with-Windows toggle, and uninstall. The
optional **advanced tier** (noise-control switching) needs a separate opt-in
driver and two machine-wide security changes; it is documented separately in the
[**advanced-tier guide**](advanced-tier.md) and summarised under
[Advanced tier (optional)](#advanced-tier-optional) below.

---

## What you need

- **Windows 11 21H2 or newer** (OS build 22621+).
- A working **Bluetooth radio** (ideally AAC-capable — see [Audio honesty](#audio-honesty-aac-vs-sbc)).
- AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max).
- **No administrator rights** for the recommended install path, and **no driver**.

---

## Install

There are two channels. The **Microsoft Store / winget** channel is the
recommended, **no-admin** path. The **manual MSIX** from GitHub Releases is a
fallback that needs a one-time, admin-only certificate-trust step.

### A. Microsoft Store / winget (recommended, no admin)

PodBridge is distributed through the Microsoft Store and installs per-user with
**no administrator prompt** (the Store pre-trusts the signature).

From a normal (non-elevated) terminal:

```powershell
winget install <StoreProductId> -s msstore --scope user
```

`<StoreProductId>` is the 12-character Store product ID (for example, shaped like
`9P6SKKFKSHKM`). It is published with the first Store release; until then, install
the manual MSIX below. You can also search **"PodBridge"** in the Microsoft Store
app and click **Get** — same no-admin install.

### B. Manual MSIX from GitHub Releases (fallback, one-time admin step)

Each release also attaches a signed `.msix` to
[GitHub Releases](https://github.com/bhemsen/PodBridge/releases). This build is
signed with a **self-signed** certificate that Windows does not trust by default,
so you must **trust the certificate once** before the package will install. This
is the reason it is a fallback and not the no-admin path.

1. Download both **`PodBridge-<tag>.msix`** and **`PodBridge-SelfSigned.cer`** from
   the release.
2. Trust the certificate once, from an **elevated (Run as administrator)**
   PowerShell, in the download folder:

   ```powershell
   Import-Certificate -FilePath .\PodBridge-SelfSigned.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Install the package (this step needs **no** admin):

   ```powershell
   Add-AppxPackage -Path .\PodBridge-<tag>.msix
   ```

Notes:

- App Installer checks the **machine** `TrustedPeople` store, which is why step 2
  is admin-only. The bundled `.cer` is the public certificate only (no private
  key). To undo trust later, remove that certificate from
  `Cert:\LocalMachine\TrustedPeople` (admin).
- PodBridge itself always runs **as your normal user** (`asInvoker`, no
  elevation). Admin is needed only for the one-time cert-trust step above — never
  for the Store channel and never at run time.

---

## Set up in under 2 minutes (to battery-visible)

1. **Launch PodBridge.** A **PodBridge** icon appears in the notification area
   (system tray). There is no main window and **no UAC prompt** — PodBridge lives
   in the tray. Hover for a tooltip like `PodBridge — … · …`.
2. **Pair your AirPods** if they are not paired yet. Right-click the tray icon and
   choose **`Pair / Reconnect`** (this opens Windows **Bluetooth & devices**
   settings). Put the AirPods in pairing mode — lid open, then press and hold the
   case button until the light blinks white — and add them from **Add device →
   Bluetooth**. On first run PodBridge also shows a one-time **"Pair your AirPods"**
   notification if none are paired.
3. **Confirm battery is visible.** Once the AirPods connect, right-click the tray
   icon: the **`Status:`** line reads **`Connected`** and the **`Battery:`** line
   shows the left bud, right bud, and case charge, for example
   `L 80% · R 75% · Case 90%⚡` (a `⚡` marks charging). The same information is in
   the tooltip. That is the ≤ 2-minute goal: **AirPods paired, audio playing,
   battery visible.**

If the `Status:` line reads **`No AirPods paired`**, **`Disconnected`**, or
**`Bluetooth unavailable`**, see [Troubleshooting](#troubleshooting).

---

## The tray menu

Right-click the tray icon. The menu (top to bottom):

- **`Status: …`** — connection state (`Connected`, `Disconnected`,
  `No AirPods paired`, or `Bluetooth unavailable`). *(display only)*
- **`Battery: …`** — left / right / case charge, or `unknown / out of range` when
  no live reading is available. *(display only)*
- **`Codec: …`** — the negotiated audio codec (see [Audio honesty](#audio-honesty-aac-vs-sbc)). *(display only)*
- **`Mic: …`** — the current microphone link mode (see [The microphone trade-off](#the-microphone-trade-off-a2dphfp)). *(display only)*
- **`Microphone mode`** — submenu to choose the mic-profile policy (below).
- **`Noise control`** — submenu to switch Off / Noise Cancellation / Transparency /
  Adaptive. Requires the optional [advanced tier](#advanced-tier-optional); until
  its driver is installed the modes are disabled with an honest explanation and an
  **`Enable advanced tier…`** entry.
- **`Refresh audio status`** — re-reads the codec and mic lines on demand.
- **`Pair / Reconnect`** — opens Windows Bluetooth settings to add or reconnect AirPods.
- **`Open Bluetooth settings`** — opens Windows Bluetooth settings.
- **`About PodBridge`** — opens the About window (disclaimer, license, version, docs link).
- **`Exit`** — quits PodBridge.

---

## Audio honesty (AAC vs SBC)

PodBridge is honest about sound quality and **never pretends to reproduce Apple's
own sound.** On supported hardware Windows plays media over **AAC**, the best
codec available on Windows — good, but produced by Windows' own encoder and not a
copy of Apple's audio processing. On hardware or drivers that only negotiate
**SBC**, quality is lower.

The tray **`Codec:`** line reports what was actually negotiated, verbatim:

- `Codec: AAC (best available on Windows)`
- `Codec: SBC`
- `Codec: couldn't determine`

When the codec is confirmed **SBC**, PodBridge shows honest, driver-free guidance:
make sure you are on Windows 11 21H2 or later, update your Bluetooth adapter
driver, and prefer an AAC-capable Bluetooth adapter or dongle. PodBridge never
offers a "force AAC" button and never recommends a paid audio driver — those are
not honest, driver-free levers.

---

## The microphone trade-off (A2DP↔HFP)

This is the single most important honesty point. On AirPods, Bluetooth Classic
cannot carry **hi-fi stereo playback (A2DP)** and a **working microphone (HFP)**
at the same time. The moment any app uses the AirPods microphone, Windows drops
the radio to the **HFP** call profile and your music collapses to **mono call
quality**. This is a **Bluetooth-Classic platform limit, not a PodBridge bug** —
no user-mode tool can give you a high-quality microphone and hi-fi stereo at once
on AirPods. **PodBridge manages which device holds the microphone role; it does
not remove the trade-off.**

The tray **`Mic:`** line reports the live state, verbatim:

- `Mic: High quality (A2DP)` — stereo/hi-fi media, AirPods mic not in use.
- `Mic: Call mode (mono)` — HFP call profile; media is mono call quality.
- `Mic: couldn't determine`

### Microphone-profile modes

Choose a mode under the tray **`Microphone mode`** submenu. It contains three
mutually-exclusive modes (a radio group), then a Call-mode toggle:

- **`HiFi-lock`** *(default)* — AirPods stay the media output; calls/mic sessions
  are pointed at a **non-AirPods** device (built-in mic, a USB headset, a webcam
  mic, …). Your AirPods music **stays hi-fi (A2DP)** even when an app opens a
  microphone. Trade-off: calls do **not** use the AirPods microphone.
- **`Auto-switch`** — AirPods media stays hi-fi until an app actually opens a
  microphone for a call; for the duration of that call the **AirPods microphone is
  used** (mono/HFP — expected), and when the call ends PodBridge **restores hi-fi
  A2DP** media on the AirPods automatically.
- **`Call-mode`** — a manual switch. Use the **`AirPods mic (Call-mode)`** toggle to
  put the AirPods microphone on/off on demand (independent of any live call). While
  on, media is mono call quality; turn it off to return to hi-fi.

Below the modes is the **`AirPods mic (Call-mode)`** toggle described above. The
selected **mode is remembered across restarts** (default `HiFi-lock`); the
`AirPods mic (Call-mode)` toggle deliberately **starts off on every launch**, so
your AirPods are never silently forced into mono at startup.

### When AirPods are your only audio device

If the AirPods are the **only** audio device (no other speaker or microphone to
hold the call role), `HiFi-lock` and `Auto-switch` cannot keep calls off the
AirPods, so they behave like `Call-mode`. PodBridge is honest about this: the
submenu shows a warning line and a one-time notification reading exactly:

```
No alternate mic — AirPods mic requires HFP/mono.
```

Plug in or enable any second audio device and the warning clears automatically.

---

## Start with Windows (auto-start)

PodBridge does **not** start automatically with Windows — auto-start is
**off by default** (least-invasive default). To have PodBridge start when you sign
in, open **`About PodBridge`** from the tray and turn on the **start-with-Windows**
option. It uses the standard MSIX startup-task mechanism, so you can also review
or disable it any time in **Task Manager → Startup apps** or **Settings → Apps →
Startup**.

---

## About window

**`About PodBridge`** in the tray opens a small window showing: the product name
**PodBridge** (no Apple logo), the **for AirPods on Windows** descriptor, the app
version, the **not-affiliated disclaimer**, the honest audio/mic note, the
**Apache-2.0** license and third-party notices, and links to the user
documentation and project page.

---

## Advanced tier (optional)

Everything above is **Tier 1** — driver-free, no admin. The optional **advanced
tier** adds **noise-control switching** (Off / Noise Cancellation / Transparency /
Adaptive) from the tray on supported AirPods (reference model AirPods Pro 2).

It is **not** part of the default experience and is **not** required for any
Tier-1 feature. Because Windows only lets a **kernel driver** reach the AirPods
noise-control channel, enabling it means installing a small driver and — honestly
— making **two machine-wide security changes** so a **test-signed** (not
Microsoft-signed) driver can load on 64-bit Windows:

1. **Enabling test-signing mode** (`bcdedit /set testsigning on` + reboot) — a
   manual step **you** perform; PodBridge never runs `bcdedit` for you.
2. **Trusting a self-signed test certificate** — the opt-in installer imports it
   into your machine's Trusted Root CA and Trusted Publishers stores.

Together these lower your machine's driver-security bar until you undo them; both
are reversible, and Tier 1 keeps working without either. PodBridge makes **no**
claim of a Microsoft-signed driver; the attestation path (EV certificate +
Partner Center) is a deferred, out-of-scope option.

The full opt-in install / uninstall flow, the security trade-off, and how to
start it from the tray (**Noise control → Enable advanced tier…**) are in the
[**advanced-tier guide**](advanced-tier.md).

---

## Uninstall

- **If you installed via the Store / winget:** run
  `winget uninstall <StoreProductId>`, or go to **Settings → Apps → Installed
  apps → PodBridge → Uninstall**. No admin needed.
- **If you installed the manual MSIX:** uninstall the same way (Settings → Apps),
  or run `Remove-AppxPackage` for the PodBridge package. To also remove the trust
  you added, delete the PodBridge self-signed certificate from
  `Cert:\LocalMachine\TrustedPeople` (admin).

PodBridge stores only a couple of small per-user settings files under
`%LOCALAPPDATA%\PodBridge` (the chosen microphone mode and first-run markers);
delete that folder if you want to remove them too. PodBridge makes **no network
calls** except an explicit, user-visible update check — it is local-only.

---

## Troubleshooting

- **`Status: No AirPods paired`** — no AirPods are paired to this PC yet. Use
  `Pair / Reconnect` and add them in Windows Bluetooth settings.
- **`Status: Disconnected`** — the AirPods are paired but not connected. Take them
  out of the case / near the PC, or use `Pair / Reconnect`.
- **`Status: Bluetooth unavailable`** — the PC's Bluetooth radio is off or missing.
  Turn Bluetooth on in Windows settings.
- **`Battery: unknown / out of range`** — no live reading (disconnected or the
  reading went stale). Reconnect the AirPods; battery re-appears once they are
  `Connected`.
- **Music dropped to mono during a call** — expected (see
  [the microphone trade-off](#the-microphone-trade-off-a2dphfp)). Use `HiFi-lock`
  to keep calls off the AirPods, or `Auto-switch` to restore hi-fi automatically
  when the call ends.

---

## Disclaimer

PodBridge is not affiliated with, authorized, sponsored, or endorsed by Apple Inc.
"AirPods" and "Apple" are trademarks of Apple Inc., used here only descriptively
to identify the hardware this software works with. PodBridge uses no Apple logo.
PodBridge is open-source software licensed under **Apache-2.0**.
