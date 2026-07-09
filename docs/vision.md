# Vision

> Normative. What and why only — no implementation detail. Keep to ~1 page;
> this file is permanently loaded via CLAUDE.md. No status marker — foundation
> docs carry none.

## Problem

AirPods pair with Windows out of the box, but the experience is second-class:
no battery readout, no automatic play/pause when you take an earbud out, no way
to switch noise-control modes or remap gestures, audio often falls back to the
lower-quality SBC codec, and using the AirPods microphone silently collapses
playback to mono call quality. Today the only tool that closes most of this gap
is **MagicPods** — closed-source and paid. Every open-source option covers only
a slice (battery-only, or battery + ear-detection with no noise-control or
gestures). There is no open, polished, one-package tool that makes AirPods feel
native on Windows without paying or fiddling.

## Why now

Apple's accessory protocol (AAP) is now thoroughly and openly reverse-engineered
(LibrePods documents the exact packets), Windows 11 gained native AAC A2DP, and
the pain point is mass-market (Windows work laptop + personal iPhone/AirPods is
an everyday combination). The pieces to build a genuinely good open tool finally
exist in one place.

## Target users

Primary: Windows 11 users who own AirPods (2 / 3 / Pro / Pro 2 / Pro 3 / Max)
and live in a mixed Apple + Windows world (e.g. iPhone plus a Windows work or
gaming PC) and want the Mac-like experience without a paid app. Secondary:
privacy/open-source-minded users who prefer an auditable tool over a closed one.

## Goal

Give Windows users an open, polished, low-friction tool that brings the AirPods
experience as close to Apple-native as Windows technically allows — great by
default with **zero admin rights and no driver**, and honest about the few
things Windows and Bluetooth make impossible.

## USP / differentiation

The one thing no alternative offers: **an open-source, polished, driver-free-by-
default AirPods companion for Windows** — battery, automatic play/pause, AAC
audio guidance and a smart microphone-profile policy that work with no admin and
no driver, plus advanced features (noise-control switching, gesture remap) as a
clearly-labelled optional add-on. It matches MagicPods' intent but is free and
auditable, and it goes further than every open tool on the microphone problem.
The ADOPT/AVOID harvest and per-entry evidence live in `docs/prior-art.md`.

## Success criteria

- **Setup:** from a fresh install to "AirPods paired, audio playing, battery
  visible" in ≤ 2 minutes, with **no admin rights and no driver** for the
  default tier.
- **Audio honesty:** the tool reports the actually-negotiated codec (AAC vs SBC)
  and, on hardware that supports it, media plays over AAC; it never claims
  Apple-identical sound.
- **Default-tier feature checklist (driver-free):** battery % for both buds and
  case; automatic play/pause on in-ear/out-of-ear; a microphone-profile policy
  offering at least "HiFi-lock", "auto-switch", and "call-mode" — each a yes/no
  observable behaviour.
- **Advanced-tier checklist (optional, opt-in):** noise-control (ANC /
  Transparency / Adaptive) switching and gesture remap function on supported
  models — behind an explicit, clearly-warned install step.
- **Invasiveness:** the default tier installs and runs without administrator
  rights or a kernel driver (verifiable); anything more invasive is opt-in.
- **Distribution:** a single open-source installer with no bundled paid or
  proprietary components.

## Scope

### In

- Guided, simple AirPods pairing / reconnect on Windows 11.
- Battery display (buds + case) and automatic play/pause via passive BLE.
- Audio-quality transparency: detect the negotiated codec, advise how to reach
  AAC, surface the active microphone mode.
- Microphone-profile policy managing the unavoidable A2DP↔HFP trade-off
  (HiFi-lock / auto-switch / call-mode).
- Optional advanced tier: ANC/Transparency/Adaptive switching and gesture remap.
- A tray-first, low-friction UI.

### Out

- Simultaneous high-quality stereo output **and** AirPods microphone input
  (physically impossible over the Bluetooth AirPods support).
- A custom audio codec driver to raise sound quality (no net benefit for
  AirPods, licensing/patent burden, invasive).
- Ear-tip fit test, Spatial Audio, and Adaptive EQ (Apple-silicon DSP / not
  reverse-engineered).
- macOS / Linux / Android targets, and non-AirPods earbuds.
- Anything requiring an Apple account or Apple software on the PC.

## Non-goals

- **Apple-identical sound** — Windows' AAC encoder is measurably weaker and
  Apple's audio DSP cannot be replicated; we target "as close as Windows allows".
- **Fixing the mic/HiFi trade-off** — it is a Bluetooth-Classic platform limit,
  not a bug; we manage it, we do not pretend to solve it.
- **Driver-by-default** — the advanced tier's kernel driver is never forced;
  invasiveness is always the user's explicit choice.
- **Using Apple's trademarks in the product name** — descriptive "for AirPods"
  only; no Apple logo; a not-affiliated disclaimer.
