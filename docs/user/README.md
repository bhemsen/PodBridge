# PodBridge user guide

**PodBridge** is an open-source companion **for AirPods on Windows** — battery,
automatic play/pause, honest audio guidance and a microphone-profile policy, all
**driver-free and with no administrator rights**.

> PodBridge is not affiliated with, authorized, sponsored, or endorsed by Apple
> Inc. "AirPods" and "Apple" are trademarks of Apple Inc., used here only
> descriptively to identify the hardware this software works with. PodBridge uses
> no Apple logo.

This guide covers the **driver-free MVP** (Tier 1): download and verify, the
≤ 2-minute fresh-run-to-battery-visible setup, the honest audio and microphone
caveats, the microphone-profile modes, the start-with-Windows toggle, and
uninstall. The
optional **advanced tier** (noise-control switching) needs a separate opt-in
driver and two machine-wide security changes; it is documented separately in the
[**advanced-tier guide**](advanced-tier.md) and summarised under
[Advanced tier (optional)](#advanced-tier-optional) below.

---

## What you need

- **Windows 11 21H2 or newer** (OS build 22621+).
- A working **Bluetooth radio** (ideally AAC-capable — see [Audio honesty](#audio-honesty-aac-vs-sbc)).
- AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max).
- **No administrator rights** — PodBridge is a self-contained exe, not an
  installer, and needs **no driver** for anything in this guide.

---

## Download and run

PodBridge ships as a **self-contained, single-file `.exe`** — no installer, no
Microsoft Store, no `winget`, no admin rights. Downloading and double-clicking
the file *is* the install.

1. Go to [GitHub Releases](https://github.com/bhemsen/PodBridge/releases) and
   open the latest release (the top one).
2. Download the exe that matches your PC's architecture:
   - **`PodBridge-<version>-win-x64.exe`** — the vast majority of Windows PCs
     (Intel/AMD).
   - **`PodBridge-<version>-win-arm64.exe`** — Windows-on-ARM devices (e.g. a
     Surface Pro X/11 or another Snapdragon-based Windows laptop).

   Not sure which you have? **Settings → System → About → Device specifications
   → System type** shows "x64-based processor" or "ARM-based processor".
3. (Recommended, takes under a minute) [Verify your download](#verify-your-download)
   before running it.
4. Run the exe directly from your Downloads folder (or move it anywhere you like
   first — there is nothing to install alongside it). See
   [Verify your download](#verify-your-download) for the SmartScreen prompt you
   should expect on first run.

That's it — no setup wizard, no reboot, no elevation prompt.

---

## Verify your download

PodBridge's release exe is **unsigned**, backed instead by a
**build-provenance attestation**, a **published checksum**, and an **SBOM** —
this is an intentional, documented trade-off (free/open-source signing that gives
instant trust does not exist outside the Microsoft Store, which 1.0 does not use;
see [`docs/vision.md`](../vision.md) and the release spec). Verifying the
download is optional but recommended, especially the first time you download a
release.

### 1. Check the checksum

Every release includes a `checksums.sha256` file listing the SHA-256 hash of
each exe. From a normal (non-elevated) PowerShell, in your Downloads folder:

```powershell
certutil -hashfile PodBridge-<version>-win-x64.exe SHA256
```

Compare the printed hash (ignore case and spacing) against the matching line in
`checksums.sha256` from the same release. They must match exactly.

### 2. Check the build-provenance attestation

The release workflow also publishes a **GitHub build-provenance attestation** for
each exe, proving it was built by PodBridge's own CI from a specific commit — not
tampered with or substituted after the fact. With the
[GitHub CLI](https://cli.github.com/) installed:

```powershell
gh attestation verify PodBridge-<version>-win-x64.exe -R bhemsen/PodBridge
```

A genuine release exe reports success; a tampered or substituted file **fails**
this check.

### 3. Expect a SmartScreen warning on first run — this is normal

Because the exe is unsigned, the **first time you run it** Windows SmartScreen
will very likely show:

> **Windows protected your PC**
> Microsoft Defender SmartScreen prevented an unrecognized app from starting.
> Running this app might put your PC at risk. **Unknown publisher.**

This is the **expected, honest** behaviour for any new, unsigned download — it is
not specific to PodBridge and it is not evidence of a problem by itself. Once
you've verified the checksum and/or attestation above, you can proceed:
click **More info**, then **Run anyway**.

**PodBridge will never tell you to blindly bypass or disable SmartScreen.**
If you have *not* verified the download and are unsure, don't run it — re-download
from the official [GitHub Releases](https://github.com/bhemsen/PodBridge/releases)
page instead. An organization-managed PC may block "Run anyway" entirely (Smart
App Control / enterprise policy) regardless of verification; that is outside
PodBridge's control.

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
in, open **`About PodBridge`** from the tray and turn on the **"Start PodBridge
automatically when I sign in"** option. This writes a per-user Run entry
(`HKEY_CURRENT_USER\...\Run`) pointing at wherever the exe currently lives — no
admin needed. You can also review or turn it off any time in **Task Manager →
Startup apps** or **Settings → Apps → Startup**; if you disable it there, PodBridge
honours that and will not silently re-enable it. If you move the exe to a new
folder, PodBridge updates the stored path the next time it launches while
auto-start is on.

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

There is nothing to "uninstall" in the traditional sense — PodBridge is a single
portable exe, not an installed package:

1. **Quit PodBridge** — right-click the tray icon and choose **`Exit`** (if
   auto-start is on, turn it off first in **`About PodBridge`**, or it will just
   start again next sign-in).
2. **Delete the exe** you downloaded.
3. **Delete the `%LOCALAPPDATA%\PodBridge` folder** — this removes the stored
   microphone-mode setting, first-run markers, diagnostics exports, and logs.

That's the whole removal; there is no registry install entry, no Store package,
and (unless you enabled auto-start) no other trace on the machine. PodBridge
makes **no network calls** except an explicit, user-visible update check — it is
local-only.

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
- **All audio devices disappear and no microphone works (typically after long
  sleep, then joining a call)** — if *every* audio output and input vanishes and
  microphones capture nothing — usually after the PC has been in sleep/hibernate
  for a long time (a day or more) and often triggered by starting a call (e.g.
  Teams) while several apps are already playing sound — this is a **Windows
  audio-stack failure, not a PodBridge bug**. The give-away is that it also takes
  down **wired outputs like line-out that PodBridge never touches**: PodBridge can
  only change *which* device is the default, it has no way to remove a device from
  Windows at all. The root cause is the Windows Audio / Audio Endpoint Builder
  service failing to re-enumerate devices after a long resume. To recover, restart
  the **Windows Audio** service (`services.msc` → *Windows Audio* → Restart) or
  reboot; reconnecting the AirPods often brings *them* back but does not restore
  the other endpoints. If it keeps happening, doing a full **shutdown** rather than
  sleep between long idle periods avoids the resume path that triggers it.

---

## Disclaimer

PodBridge is not affiliated with, authorized, sponsored, or endorsed by Apple Inc.
"AirPods" and "Apple" are trademarks of Apple Inc., used here only descriptively
to identify the hardware this software works with. PodBridge uses no Apple logo.
PodBridge is open-source software licensed under **Apache-2.0**.
