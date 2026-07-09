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
| 1 | Foundation & pairing — solution skeleton, DI/host, WPF tray shell, guided pairing/reconnect UX, CI + Verify command | [spec](docs/specs/archive/spec-foundation-pairing.md) | [#1](https://github.com/bhemsen/PodBridge/milestone/1) |
| 2 | Battery & auto play/pause — WinRT BLE scanner + Apple-Continuity parser, tray battery (buds+case), in-ear media control | [spec](docs/specs/archive/spec-battery-status.md) | [#2](https://github.com/bhemsen/PodBridge/milestone/2) |
| 3 | Audio transparency — negotiated-codec detection (AAC/SBC), active mic-mode display, guidance on reaching AAC | [spec](docs/specs/archive/spec-audio-transparency.md) | [#3](https://github.com/bhemsen/PodBridge/milestone/3) |
| 4 | Microphone-profile policy — HiFi-lock / auto-switch / call-mode via IPolicyConfig + audio-session monitor | [spec](docs/specs/spec-mic-profile-policy.md) | [#4](https://github.com/bhemsen/PodBridge/milestone/4) |
| 5 | Packaging & distribution — MSIX + winget installer, disclaimer, docs, Apache-2.0, first driver-free release (MVP) | [spec](docs/specs/spec-packaging-distribution.md) | [#5](https://github.com/bhemsen/PodBridge/milestone/5) |
| 6 | Advanced tier: KMDF L2CAP driver + AAP writes — optional driver, ANC/Transparency/Adaptive switching, signing/test-mode UX | [spec](docs/specs/spec-advanced-driver-anc.md) | [#6](https://github.com/bhemsen/PodBridge/milestone/6) |
| 7 | Gesture remap — stem/press configuration via AAP, re-push on reconnect | [spec](docs/specs/spec-gesture-remap.md) | [#7](https://github.com/bhemsen/PodBridge/milestone/7) |
| 8 | Model & firmware coverage / hardening — broaden supported models, handle firmware fragility, diagnostics | [spec](docs/specs/spec-model-coverage-hardening.md) | [#8](https://github.com/bhemsen/PodBridge/milestone/8) |

Phases 1–5 are the **driver-free MVP** (the bulk of the value, low risk).
Phases 6–8 are the **opt-in advanced tier** (kernel driver, higher risk — gated
on the driver-signing reality documented in `docs/prior-art.md`).

A phase gets a Spec link once `/plan` drafts it, and a Milestone link once the
spec is merged. The milestone (open/closed + issue progress) is where status
lives.

## North star

AirPods feel native on Windows — great by default with no driver or admin, and
honest about the few things Bluetooth and Windows make impossible.
