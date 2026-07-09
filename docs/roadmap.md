# PodBridge — Roadmap

> Living document: the sequenced queue of phases. The hand-off to `/plan`, which
> picks the next phase, creates its spec + issues, and links them back here.
> No status markers — progress lives in the GitHub issues and milestones each
> phase links to. Specs (created by `/plan`) carry no lifecycle state either;
> a spec is "accepted" once merged on the default branch with a milestone and
> issues.

## Phase overview

| Phase | Name | Spec | Milestone |
|---|---|---|---|
| 1 | Foundation & pairing — solution skeleton, DI/host, WPF tray shell, guided pairing/reconnect UX, CI + Verify command | [spec](docs/specs/spec-foundation-pairing.md) | [#1](https://github.com/bhemsen/PodBridge/milestone/1) |
| 2 | Battery & auto play/pause — WinRT BLE scanner + Apple-Continuity parser, tray battery (buds+case), in-ear media control | — | — |
| 3 | Audio transparency — negotiated-codec detection (AAC/SBC), active mic-mode display, guidance on reaching AAC | — | — |
| 4 | Microphone-profile policy — HiFi-lock / auto-switch / call-mode via IPolicyConfig + audio-session monitor | — | — |
| 5 | Packaging & distribution — MSIX + winget installer, disclaimer, docs, Apache-2.0, first driver-free release (MVP) | — | — |
| 6 | Advanced tier: KMDF L2CAP driver + AAP writes — optional driver, ANC/Transparency/Adaptive switching, signing/test-mode UX | — | — |
| 7 | Gesture remap — stem/press configuration via AAP, re-push on reconnect | — | — |
| 8 | Model & firmware coverage / hardening — broaden supported models, handle firmware fragility, diagnostics | — | — |

Phases 1–5 are the **driver-free MVP** (the bulk of the value, low risk).
Phases 6–8 are the **opt-in advanced tier** (kernel driver, higher risk — gated
on the driver-signing reality documented in `docs/prior-art.md`).

A phase gets a Spec link once `/plan` drafts it, and a Milestone link once the
spec is merged. The milestone (open/closed + issue progress) is where status
lives.

## North star

AirPods feel native on Windows — great by default with no driver or admin, and
honest about the few things Bluetooth and Windows make impossible.
