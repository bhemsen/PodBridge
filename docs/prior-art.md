# Prior Art

> Descriptive, living document. Indexed BY CONCERN, not by project. Add
> entries whenever new references surface; gaps are fine.
> Research + adversarial verification pass: 2026-07-08 (8 axes, 4 verified claims).

## Full AirPods-on-Windows companion (end-user tools)

### steam3d/MagicPods-Windows (product: MagicPods)

- Path: https://github.com/steam3d/MagicPods-Windows · https://magicpods.app · Store id 9P6SKKFKSHKM
- License: Proprietary / paid (~$1.99–3.99 one-time, Microsoft Store)
- Verdict: reference-only — closed source, cannot be reused; the only tool covering the FULL feature set.
- Date: 2026-07-08
- Notes:
  - ADOPT: the UX/feature bar to beat (battery, ear detection, ANC/transparency, gesture customization, conversation awareness, low-latency). Its `MagicAAP` kernel driver is direct proof the L2CAP feature tier needs a driver on Windows.
  - AVOID: copying any code (proprietary); assuming driver signing is solved — even MagicPods ships MagicAAP unsigned / test-mode and its community cross-signed build is broken by the April-2026 Windows signing policy.

### SpriteOvO/AirPodsDesktop

- Path: https://github.com/SpriteOvO/AirPodsDesktop · `Source/Core/Bluetooth_win.cpp`, `Source/Core/AppleCP.h` (Apple Continuity parsing)
- License: GPL-3.0 (C++/Qt)
- Verdict: reuse (patterns) — best open, actively-maintained (v0.4.2 Beta, Mar 2026), Windows-native, driver-free base. Battery + in-ear detection + low-latency only.
- Date: 2026-07-08
- Notes:
  - ADOPT: the driver-free BLE-advertisement path is *proven* here — WinRT `BluetoothLEAdvertisementWatcher` + Apple Continuity (0x004C) parsing → battery + in-ear + auto play/pause with no admin/driver. Reference the patterns; the exact license fork (copy vs reimplement) is a constitution decision.
  - AVOID: expecting ANC/transparency/gesture control (deliberately absent — not obtainable from advertisements); copying GPL code into a permissive project (forces GPL-3.0).

### YimingZhanshen/AirPodsWindows

- Path: https://github.com/YimingZhanshen/AirPodsWindows
- License: GPL-3.0 (fork of AirPodsDesktop)
- Verdict: reference-only — adds ANC/Transparency/Adaptive + conversation awareness on Windows, but only via the separately-installed MagicAAP driver; no gesture remap; small (~9 stars).
- Date: 2026-07-08
- Notes:
  - ADOPT: proof that open-source ANC control on Windows is possible when the driver problem is delegated.
  - AVOID: hard dependency on MagicPods' proprietary MagicAAP driver.

### Battery/status-only tools (winpods, AirStatus, OpenPods, CAPoD, ToothTray, generic BT battery monitors)

- Path: https://github.com/sinanovicanes/winpods (MIT, Rust/Tauri) · https://github.com/delphiki/AirStatus · https://github.com/adolfintel/OpenPods · https://github.com/d4rken-org/capod
- License: mixed (MIT / GPL-family)
- Verdict: reference-only — single-function (battery via BLE advertisement) or connect/disconnect helpers.
- Date: 2026-07-08
- Notes:
  - ADOPT: winpods = clean modern Rust+Tauri tray reference; CAPoD = mature BLE-advertisement battery decode; AirStatus/OpenPods = minimal BLE battery-beacon parsing.
  - AVOID: adopting any as the base (feature-thin, several stalled after mid-2025).

## AAP / AACP protocol reverse-engineering

### librepods-org/librepods (kavishdevar)

- Path: https://github.com/librepods-org/librepods · `docs/AAP Definitions.md`, `docs/opcodes.md`, `docs/control_commands.md` + Wireshark dissector
- License: GPL-3.0-or-later (NOT AGPL) — name/logo use restricted
- Verdict: reference-only (docs = reuse as facts) — Android+Linux only, no Windows; the definitive open AAP reference.
- Date: 2026-07-08
- Notes:
  - ADOPT: the protocol *facts* — L2CAP PSM 0x1001 (4097), plaintext 16-byte handshake, notification-register packet, battery opcode 0x04, in-ear 0x06, ANC/Transparency/Adaptive read+set via 0x09/0x0D (Off=01/ANC=02/Transp=03/Adaptive=04), gesture remap 0x14/0x15/0x16, conversation awareness 0x28/0x4B, rename 0x1E/0x1A. Reimplement clean-room from these facts.
  - AVOID: copying source code or verbatim doc *prose* (GPL-encumbered); relying on its L2CAP transport (Android BluetoothSocket / Linux BlueZ — the exact layer Windows does not expose). Spec is pinned to AirPods Pro 2 USB-C fw 7A305 — firmware-fragile; ear-tip fit test is NOT reverse-engineered (genuine gap).

## Windows Bluetooth app access — the L2CAP feasibility wall

### Microsoft in-box Bluetooth stack (WinRT / WinSock AF_BTH)

- Path: https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/creating-a-l2cap-client-connection-to-a-remote-device · OSR thread https://community.osr.com/t/.../49120
- License: n/a (platform)
- Verdict: avoid (as an AAP transport) — user mode gets RFCOMM + BLE/GATT only; custom Classic-L2CAP PSM connections require a kernel-mode KMDF profile driver (`BRB_L2CA_OPEN_CHANNEL`). Confirmed by adversarial verification.
- Date: 2026-07-08
- Notes:
  - ADOPT: the driver-free tier — WinRT `BluetoothLEAdvertisementWatcher` (battery, in-ear), standard A2DP/HFP audio via normal pairing, RFCOMM if ever needed.
  - AVOID: 32feet.NET for AAP L2CAP (its L2CAP classes work only on the legacy Widcomm stack, not the modern MS stack); assuming any user-mode L2CAP PSM path exists (none as of 2025/2026 docs).

### changcheng967/WinPods (driver-based)

- Path: https://github.com/changcheng967/WinPods · `driver/WinPodsAAP/` (KMDF L2CAP bridge, C) + `src/WinPods.Core/AAP/`
- License: MIT (verify) — C#/.NET 10 + WinUI 3
- Verdict: reference-only — clearest open blueprint for the driver tier; driver incomplete/unverified on real hardware as of v1.2.0 (Mar 2026).
- Date: 2026-07-08
- Notes:
  - ADOPT: the architectural split (driver-free BLE tier vs KMDF L2CAP-bridge tier); MIT-licensed AAP implementation reference.
  - AVOID: depending on it as-is (young, driver unverified, needs test-signing + admin).

## Windows audio codec & quality

### Microsoft "Bluetooth Classic Audio" (native codec support)

- Path: https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio
- License: n/a (platform)
- Verdict: reuse — Windows 11 21H2+ natively negotiates AAC A2DP (no driver). Confirmed by adversarial verification.
- Date: 2026-07-08
- Notes:
  - ADOPT: native AAC is the AirPods quality ceiling on Windows 11 — no third-party driver needed for AirPods' best codec. Tool can detect the negotiated codec and advise driver/dongle updates on SBC fallback.
  - AVOID: promising Apple-parity sound (Windows AAC encoder measurably worse — Archimago 2023); Windows 10 (no AAC at all); a "force AAC" claim (no documented switch; negotiation is radio/driver-dependent).

### bluetoothgoodies.com "Alternative A2DP Driver"

- Path: https://www.bluetoothgoodies.com/a2dp/
- License: Proprietary / paid (per-PC, hardware-tied)
- Verdict: avoid — adds aptX/LDAC (unusable by AirPods) and AAC (already native in Win11); zero net benefit for AirPods; admin + driver replacement; not redistributable.
- Date: 2026-07-08
- Notes:
  - AVOID: bundling or recommending it for AirPods; a custom AAC codec driver (APO/AVStream) ambition (high effort, AAC patent pool, no OSS precedent).

## Microphone & audio-profile switching

### Windows audio-policy plumbing (IPolicyConfig, IMMDevice, IAudioSessionManager2)

- Path: https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio · IPolicyConfig header https://github.com/tartakynov/audioswitch/blob/master/IPolicyConfig.h
- License: n/a (platform) / reversed headers MIT-ish
- Verdict: reuse (patterns) — the driver-free levers for a mic-profile policy.
- Date: 2026-07-08
- Notes:
  - ADOPT: Win11 unifies A2DP/HFP into one render + one capture endpoint; HFP is forced when any app opens the mic OR opens a `Communications`-category render stream. A user-mode tool cannot command the profile but CAN set default vs default-communications endpoints per role (`IPolicyConfig::SetDefaultEndpoint`), enable/disable endpoints, and watch `IAudioSessionManager2` events. Strategies: HiFi-lock (AirPods stay A2DP, comms mic = other device), Auto-switch (on comms session), Call-mode toggle (manual, most robust).
  - AVOID: promising forced wideband mic (mSBC vs narrowband is radio-negotiated); expecting LE Audio/LC3 super-wideband (24H2) to help — AirPods are Classic-only, so it never applies.

### Reference switchers (Reconnect-AirPods, SoundSwitch, EarTrumpet, AudioDeviceCmdlets)

- Path: https://github.com/limin112/Reconnect-AirPods · https://github.com/frgnca/AudioDeviceCmdlets
- License: mixed OSS
- Verdict: reference-only — proven `IPolicyConfig` role-switching patterns; none is an integrated AirPods-aware auto-switch (genuine gap).
- Date: 2026-07-08
- Notes:
  - ADOPT: Reconnect-AirPods = concrete embedded-C# `IPolicyConfig` prior art; SoundSwitch = hotkey that swaps playback+communication device together (call-mode-toggle model).
  - AVOID: assuming a robust auto-switcher exists to fork — it doesn't.

## Implementation stack precedent

- Path: AirPodsDesktop (C++/Qt5.15, CMake+vcpkg, NSIS) · changcheng967/WinPods (C#/.NET10, WinUI3, KMDF driver) · sinanovicanes/winpods (Rust/Tauri) · IPolicyConfig via NAudio/CoreAudio
- License: mixed
- Verdict: reference-only.
- Date: 2026-07-08
- Notes:
  - ADOPT: C#/.NET is the pragmatic core (best WinRT projections via CsWinRT, tray, packaging); WPF/WinForms tray shell (WinUI 3 tray support is weak); separate C/C++ KMDF driver only for the optional tier. Audio default-device switching everywhere via undocumented `IPolicyConfig`/`IPolicyConfig2` P/Invoke (NAudio enumerates but cannot set defaults).
  - AVOID: WinUI 3 for a tray-first app; 32feet.NET for the core BT need; assuming MSIX can bundle a kernel driver (it cannot cleanly).

## Legal & licensing (not legal advice)

- Path: EFF RE FAQ; Google v. Oracle (SCOTUS 2021); EU Directive 2009/24 Art. 6; Apple 3rd-party trademark guidelines; MS Windows Driver Policy (Apr 2026)
- License: n/a
- Verdict: reference-only.
- Date: 2026-07-08
- Notes:
  - ADOPT: reimplement AAP from documented *facts* (interoperability-protected: Sega/Sony/Oracle, EU decompilation right); coined product name + "for AirPods" descriptor (no "AirPods"/"Apple" in the name, no Apple logo, disclaimer); no documented Apple enforcement against this tool class (low takedown risk). License fork: Apache-2.0 (clean-room) vs GPL-3.0 (AirPodsDesktop fork) — decide in the constitution.
  - AVOID: breaking MagicPairing crypto (use the cleartext AAP control channel); copying GPL code/prose without accepting copyleft; bundling FDK-AAC (AAC patent pool); assuming a free OSS project can easily get a Microsoft-signed kernel driver post-April-2026 (EV cert ~$250–560/yr + Partner Center friction; even paid MagicPods ships unsigned).
