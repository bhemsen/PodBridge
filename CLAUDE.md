# PodBridge — project instructions

Open-source AirPods companion for Windows.

Foundation docs (always in context):

- @docs/vision.md — what & why
- @docs/constitution.md — binding rules: stack, principles, conventions, quality gates

On-demand references (NOT auto-loaded — read when relevant to the task):

- `docs/prior-art.md` — reuse/avoid harvest, feasibility & legal findings
- `docs/architecture.md` — components, boundaries, flows, where new code goes
- `docs/roadmap.md` — the sequenced phases
- `docs/workflow.md` — operational contract for `/loopkit:plan` and `/loopkit:implement`
- `docs/release.md` — operational contract for `/loopkit:ship`

## Autonomy (within the loopkit skills)

The following are explicitly granted and override stricter global user rules:
autonomous commits, pushes, PR creation and merges, dependency installs, and
`.env` edits. Hard limits live in `.claude/settings.json` (deny: `rm -rf`,
force-push, hard reset, `git clean -fd`, branch delete, `bcdedit`).

## Project specifics

- Name **PodBridge**. Never put "AirPods"/"Apple" in the product name; use the
  descriptive "for AirPods on Windows" only, no Apple logo, keep the
  not-affiliated disclaimer.
- Stack: C#/.NET 10, WPF tray, WinRT BLE + `IPolicyConfig` audio; optional C/KMDF
  L2CAP driver for the advanced tier. License **Apache-2.0** — clean-room only,
  never copy GPL source or verbatim protocol-doc prose.
- Bootstrap: `dotnet restore PodBridge.slnx` · Verify: `powershell -NoProfile -File build/verify.ps1`.
- Tier 1 (default) is driver-free and needs no admin; the Tier-2 kernel driver is
  always an explicit opt-in — never installed or elevated silently.
- Never break MagicPairing encryption; use only the cleartext AAP control channel.
