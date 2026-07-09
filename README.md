# PodBridge

**PodBridge** is an open-source companion **for AirPods on Windows** — battery,
automatic play/pause, honest audio-quality guidance, and a smart
microphone-profile policy, **driver-free and with no administrator rights**.
Advanced features (noise-control switching, gesture remap) are an optional,
clearly-labelled add-on.

> PodBridge is **not affiliated** with, authorized, sponsored, or endorsed by
> Apple Inc. "AirPods" and "Apple" are trademarks of Apple Inc., used here only
> descriptively to identify the hardware this software works with. PodBridge uses
> no Apple logo.

## Quickstart

**Requires Windows 11 21H2+** (build 22621+). Install with **no admin rights** from
the Microsoft Store via winget:

```powershell
winget install <StoreProductId> -s msstore --scope user
```

`<StoreProductId>` is the 12-character Store product ID, published with the first
Store release; you can also search **"PodBridge"** in the Microsoft Store app. A
signed **manual MSIX** is attached to each
[GitHub Release](https://github.com/bhemsen/PodBridge/releases) as a fallback (it
needs a one-time, admin-only certificate-trust step — see the
[user guide](docs/user/README.md#b-manual-msix-from-github-releases-fallback-one-time-admin-step)).

**Set up in under 2 minutes:** launch PodBridge (a tray icon appears — no window,
no UAC prompt) → right-click the icon and choose **`Pair / Reconnect`** to add
your AirPods in Windows Bluetooth settings → once they connect, the tray
**`Status:`** line reads **`Connected`** and the **`Battery:`** line shows
left/right/case charge. That's it: **paired, playing, battery visible.**

Full instructions — install, setup, mic modes, auto-start, uninstall — are in the
**[user guide](docs/user/README.md)**.

## Scope & honesty

PodBridge brings the AirPods experience as close to native as Windows technically
allows. It is honest about the limits and **never pretends to reproduce Apple's
own sound**: on supported hardware Windows plays media over **AAC**, the best
codec available on Windows, and drops to **SBC** on hardware that lacks it.
PodBridge also cannot provide simultaneous hi-fi stereo output **and** AirPods
microphone input — using the AirPods mic forces the Bluetooth **HFP** call profile
(mono call quality). That **A2DP↔HFP** trade-off is a Bluetooth-Classic platform
limit, not a bug; PodBridge manages it (see
[the user guide](docs/user/README.md#the-microphone-trade-off-a2dphfp) and
[`docs/vision.md`](docs/vision.md)), it does not solve it.

## Building

Requires the .NET 10 SDK.

    dotnet restore PodBridge.slnx
    powershell -NoProfile -File build/verify.ps1

## Documentation

- **[User guide](docs/user/README.md)** — install, setup, mic modes, auto-start, uninstall.
- [`docs/`](docs/) — vision, architecture, roadmap, and per-phase notes.

## License

Apache-2.0. See [`LICENSE`](LICENSE), [`NOTICE`](NOTICE), and
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Disclaimer

Not affiliated with, authorized, or endorsed by Apple Inc. "AirPods" and "Apple"
are trademarks of Apple Inc., used here only descriptively. PodBridge uses no
Apple logo.
