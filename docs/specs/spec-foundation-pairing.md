# Spec: Foundation & Pairing (Phase 1)

> Created: 2026-07-09

Turn the seed skeleton into a runnable, single-instance, tray-resident PodBridge
app on the .NET generic host — Core wired to Windows adapters via DI — that
detects whether AirPods are paired/connected on Windows and guides the user to
pair or reconnect, plus a CI pipeline that runs Verify on every push. This spec
carries no lifecycle state — acceptance is the spec merged on the default branch
with a milestone and issues; all progress lives in the GitHub issues and
milestone. A completed spec is moved to `docs/specs/archive/`.

## Outcome

- [ ] PodBridge runs as a **single-instance, tray-resident** background app
      (starts to the tray without stealing focus, exits cleanly) built on
      `Microsoft.Extensions.Hosting`, with Core interfaces bound to Windows
      adapters via DI at the composition root.
- [ ] The solution contains `PodBridge.App` (WPF) and `PodBridge.Windows`
      (adapters) in addition to `PodBridge.Core` + tests, and **Verify stays
      green**.
- [ ] A **tray icon** shows AirPods connection status and a context menu:
      status line, "Pair / Reconnect", "Open Bluetooth settings", "Exit".
- [ ] The app **detects whether an AirPods device is paired and connected** via
      WinRT and updates the tray status **live** on connect/disconnect.
- [ ] **First-run guidance:** when no AirPods are paired, the app guides the
      user to pair by deep-linking to Windows Bluetooth settings.
- [ ] **CI** (GitHub Actions, `windows-latest`) runs Bootstrap + Verify on every
      push/PR and is green on `main`.
- [ ] The app runs with an **`asInvoker` manifest** (no elevation) — Tier 1
      stays driver-free and admin-free.

## Scope

### In scope

- `PodBridge.App` (WPF) + `PodBridge.Windows` (adapters) projects; generic-host
  + DI composition root; single-instance + start-to-tray lifecycle.
- Tray icon + context menu + clean shutdown.
- WinRT-based detection of a paired/connected AirPods device (connection
  status), exposed to Core behind an interface (e.g. `IConnectionMonitor`).
- First-run pairing guidance (deep-link `ms-settings:bluetooth`).
- GitHub Actions CI running the project's Verify command.

### Out of scope

- Battery %, in-ear state, or any BLE-advertisement telemetry — **Phase 2**.
- Codec/audio detection and microphone-profile policy — **Phases 3–4**.
- ANC/transparency, gesture remap, the L2CAP kernel driver — **Phases 6–7**.
- Full settings window, installer/packaging, winget — **Phase 5**.
- Company-id-based multi-model AirPods identification — **Phase 2** refines it
  (Phase 1 uses a name heuristic).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10, WPF tray, `Core` OS-free, adapters in `Windows`, composition
  root in `App`, Apache-2.0, warnings-as-errors, max 50-line functions).
- Component boundaries per `docs/architecture.md` — `App` depends on `Core`
  abstractions and wires `Windows` implementations at the composition root only.
- Tier 1 needs no admin/driver: `asInvoker` manifest; behave gracefully when no
  AirPods are present.
- Verify = `powershell -NoProfile -File build/verify.ps1` (~10s baseline); CI
  must run it on `windows-latest`.

## Prior art

- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — WPF tray + a NotifyIcon library; C#/.NET; avoid WinUI 3 for the tray.
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — the driver-free Tier-1 surface (WinRT device APIs) that connection detection
  uses; confirms no driver/admin is needed here.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — AirPodsDesktop as a reference for tray UX and Windows integration patterns.

## Human prerequisites

- [ ] none — no secrets, accounts, or external provisioning. GitHub Actions uses
      the repository's default `GITHUB_TOKEN`.

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Generic host (`Microsoft.Extensions.Hosting`) as the app backbone | Standard DI + lifecycle for a background/tray app; keeps the composition-root boundary clean | 2026-07-09 |
| Tray via `H.NotifyIcon.Wpf` | Maintained, modern NotifyIcon library for WPF; prior-art flags WinUI 3 tray support as weak | 2026-07-09 |
| Phase 1 detects paired/connected state via WinRT (`DeviceInformation`/`BluetoothDevice`), **not** BLE-advertisement telemetry | BLE battery/ear parsing is Phase 2 per the roadmap tiering; Phase 1 needs only connection status + pairing guidance | 2026-07-09 |
| Phase-1 AirPods identification is heuristic (device name contains "AirPods"/"Beats") | Company-id identification arrives with the BLE path in Phase 2; a name heuristic is enough to drive status + guidance now | 2026-07-09 |
| `asInvoker` manifest (no elevation) | Constitution: Tier 1 needs no admin | 2026-07-09 |
| OPEN — first-run/onboarding UX depth: tray + notifications only, or a minimal onboarding window? | resolved at the spec-acceptance gate | — |
| OPEN — auto-start at Windows login in Phase 1, or defer to packaging (Phase 5)? | resolved at the spec-acceptance gate | — |

## Tracking

- Milestone: created on merge (one per this phase)
- Issues: created from this spec once merged (one per implementable step)

The decomposition into steps lives as GitHub issues, not here.

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and unit tests all green.
- [ ] App launches to the tray without stealing focus; a **second launch does
      not start a second instance**.
- [ ] Tray context menu shows: status line, "Pair / Reconnect", "Open Bluetooth
      settings", "Exit"; **Exit terminates cleanly** (no lingering process).
- [ ] With AirPods paired + connected the tray shows "connected"; disconnecting
      updates it to "disconnected" **live** (and vice versa).
- [ ] With no AirPods paired, "Pair / Reconnect" opens Windows Bluetooth
      settings (`ms-settings:bluetooth`).
- [ ] CI workflow runs on push/PR on `windows-latest` and is **green on `main`**.
- [ ] The process runs **without elevation** (`asInvoker`) — no admin prompt.
- [ ] **(Human QA gate)** Verified on a real machine with real AirPods:
      connect/disconnect reflected in the tray; the pairing deep-link works.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| WinRT paired/connected detection differs across Windows builds | Hide behind `IConnectionMonitor` with a fake for unit tests; verify on real hardware at the milestone QA gate. |
| Tray library adds runtime/build heft or compat issues | `H.NotifyIcon.Wpf` is lightweight and maintained; keep it isolated in `PodBridge.App`. |
| CI cannot exercise Bluetooth | CI runs Verify (build/format/unit) only; device behavior is checked at the human QA gate (contract QA-gate default = UI check / smoke test). |
| Name-heuristic AirPods detection has false positives/negatives | Acceptable for Phase-1 status/guidance; Phase 2 replaces it with Apple company-id matching. |

## Decision log

- 2026-07-09: Spec drafted. Two genuinely-open items deferred to the
  spec-acceptance gate (onboarding depth; auto-start at login). WinRT
  connection-detection may be split into a research issue at issue-creation time
  if the API surface needs ≥3 source lookups (contract: research-intensive).
