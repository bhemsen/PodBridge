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
- [ ] Connection-status logic is covered by a **device-independent unit test**:
      a fake `IConnectionMonitor` drives connect/disconnect and the test asserts
      the tray-status mapping (constitution's Tier-1 test gate).
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
  status), exposed to Core behind an interface (`IConnectionMonitor`).
- First-run pairing guidance (deep-link `ms-settings:bluetooth`).
- GitHub Actions CI running the project's Verify command.

### Out of scope

- Battery %, in-ear state, or any BLE-advertisement telemetry — **Phase 2**.
- Codec/audio detection and microphone-profile policy — **Phases 3–4**.
- ANC/transparency, gesture remap, the L2CAP kernel driver — **Phases 6–7**.
- Full settings window, installer/packaging, winget, and the user-facing
  **not-affiliated disclaimer / About surface** — **Phase 5**.
- Company-id-based multi-model AirPods identification — **Phase 2** refines it
  (Phase 1 uses a name heuristic).

## Constraints

- Stack, layering, license, and quality principles per `docs/constitution.md`
  (C#/.NET 10, WPF tray, `Core` OS-free, adapters in `Windows`, composition
  root in `App`, Apache-2.0, warnings-as-errors, max 50-line functions).
- Component boundaries per `docs/architecture.md` — `App` depends on `Core`
  abstractions and wires `Windows` implementations at the composition root only.
  This phase adds `IConnectionMonitor` (Core) + `WinRtConnectionMonitor`
  (Windows); `docs/architecture.md` is updated to list them when they are
  implemented (living doc).
- Tier 1 needs no admin/driver: `asInvoker` manifest.
- **Graceful degradation** (constitution): with **no AirPods paired** the tray
  shows the pairing-guidance state; with **no Bluetooth radio present at all**
  the app still runs and the tray shows a "Bluetooth unavailable" state — never
  crashes.
- A **second launch** does not start a second instance: it surfaces a
  "PodBridge is already running" tray notification and exits.
- Verify = `powershell -NoProfile -File build/verify.ps1` (~10s baseline); CI
  must run it on `windows-latest`.

## Prior art

- [Implementation stack precedent](../prior-art.md#implementation-stack-precedent)
  — WPF tray + a NotifyIcon library; C#/.NET; avoid WinUI 3 for the tray.
- [Windows Bluetooth app access — the L2CAP feasibility wall](../prior-art.md#windows-bluetooth-app-access--the-l2cap-feasibility-wall)
  — confirms the driver-free / no-admin Tier-1 surface. This phase uses WinRT
  `DeviceInformation`/`BluetoothDevice` for paired/connected **status**; the
  entry's BLE `AdvertisementWatcher`/A2DP ADOPT is the Phase-2 telemetry path,
  not this one.
- [Full AirPods-on-Windows companion (end-user tools)](../prior-art.md#full-airpods-on-windows-companion-end-user-tools)
  — AirPodsDesktop as a reference for tray UX / Windows integration **patterns
  only**; it is GPL-3.0, so no code or verbatim prose may be reused (Apache-2.0
  clean-room, per constitution).

## Human prerequisites

- [ ] none — no secrets, accounts, or external provisioning. GitHub Actions uses
      the repository's default `GITHUB_TOKEN`. (Real-AirPods hardware is exercised
      at the human QA gate, not a provisioning prerequisite.)

## Prior decisions

| Decision | Rationale | Date |
|---|---|---|
| Generic host (`Microsoft.Extensions.Hosting`) as the app backbone | Standard DI + lifecycle for a background/tray app; keeps the composition-root boundary clean | 2026-07-09 |
| Tray via `H.NotifyIcon.Wpf` | Maintained, modern NotifyIcon library for WPF; prior-art flags WinUI 3 tray support as weak | 2026-07-09 |
| Phase 1 detects paired/connected state via WinRT (`DeviceInformation`/`BluetoothDevice`), **not** BLE-advertisement telemetry | BLE battery/ear parsing is Phase 2 per the roadmap tiering; Phase 1 needs only connection status + pairing guidance | 2026-07-09 |
| Phase-1 AirPods identification is heuristic (device name contains "AirPods"/"Beats") | Company-id identification arrives with the BLE path in Phase 2; a name heuristic is enough to drive status + guidance now | 2026-07-09 |
| The WinRT connection-detection unit of work is **research-intensive** → split into a `chore:research-connection-detection` issue (WinRT paired/connected API + reliability across Windows 11 builds) plus an implementation issue that `Depends on` it | Contract pre-classifies Windows BLE/device-API confirmation as research-intensive; the risk table flags cross-build variance | 2026-07-09 |
| The not-affiliated disclaimer / About surface ships in **Phase 5** (packaging) | Constitution requires the disclaimer, but Phase 1 is tray-only with no About/settings window; Phase 5 owns the user-facing surface | 2026-07-09 |
| `asInvoker` manifest (no elevation) | Constitution: Tier 1 needs no admin | 2026-07-09 |
| First-run/onboarding UX is **tray + Windows notifications only** (no separate onboarding window) | Leanest, fits low-invasiveness; the full settings/About window is Phase 5 | 2026-07-09 |
| Auto-start at Windows login is **deferred to Phase 5 (packaging)** | The installer/MSIX owns start-at-login (with an on/off option); Phase 1 stays focused | 2026-07-09 |

## Tracking

- Milestone: created on merge (one per this phase)
- Issues: created from this spec once merged (one per implementable step)

The decomposition into steps lives as GitHub issues, not here.

## Verification

- [ ] **Verify passes** (`powershell -NoProfile -File build/verify.ps1`) — build,
      format check, and unit tests all green.
- [ ] A **fake `IConnectionMonitor`** drives a unit test asserting connect →
      "connected" and disconnect → "disconnected" tray-status mapping
      (device-independent; constitution Tier-1 test gate).
- [ ] The **WinRT connection-detection research comment** (from the
      `chore:research-connection-detection` issue) is posted and its consensus is
      reflected in the implementation (contract: research-comment as QA artefact).
- [ ] App launches to the tray without stealing focus; a **second launch
      surfaces "already running" and exits** (no second instance).
- [ ] Tray context menu shows: status line, "Pair / Reconnect", "Open Bluetooth
      settings", "Exit"; **Exit terminates cleanly** (no lingering process).
- [ ] With AirPods paired + connected the tray shows "connected"; disconnecting
      updates it to "disconnected" **live** (and vice versa).
- [ ] With no AirPods paired, "Pair / Reconnect" opens Windows Bluetooth
      settings (`ms-settings:bluetooth`).
- [ ] With **no Bluetooth radio** present, the app runs and the tray shows
      "Bluetooth unavailable" — no crash.
- [ ] CI workflow runs on push/PR on `windows-latest` and is **green on `main`**.
- [ ] The process runs **without elevation** (`asInvoker`) — no admin prompt.
- [ ] **(Human QA gate)** Verified on a real machine with real AirPods:
      connect/disconnect reflected in the tray; the pairing deep-link works.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| WinRT paired/connected detection differs across Windows builds | Confirmed up front by the research issue; hidden behind `IConnectionMonitor` with a fake for unit tests; verified on real hardware at the milestone QA gate. |
| Tray library adds runtime/build heft or compat issues | `H.NotifyIcon.Wpf` is lightweight and maintained; keep it isolated in `PodBridge.App`. |
| CI cannot exercise Bluetooth | CI runs Verify (build/format/unit) only; device behavior is checked at the human QA gate (contract QA-gate default = UI check / smoke test). |
| Name-heuristic AirPods detection has false positives/negatives | Acceptable for Phase-1 status/guidance; Phase 2 replaces it with Apple company-id matching. |

## Decision log

- 2026-07-09: Spec drafted. Two genuinely-open items deferred to the
  spec-acceptance gate (onboarding depth; auto-start at login).
- 2026-07-09: Addressed the spec-review (PR #1) blocking findings — added the
  device-independent connection-status unit test as an explicit outcome +
  verification item, and committed the WinRT connection-detection unit to the
  research-intensive split (`chore:research-connection-detection`) with its
  research comment named as a QA artefact. Non-blocking: pinned second-launch
  behaviour and the no-Bluetooth-radio edge case, clarified the prior-art notes
  (WinRT `DeviceInformation` vs BLE; AirPodsDesktop GPL patterns-only), gave the
  not-affiliated disclaimer a home (Phase 5), and noted the `IConnectionMonitor`
  /`WinRtConnectionMonitor` architecture addition (recorded in
  `docs/architecture.md` when implemented).
- 2026-07-09: Spec-acceptance gate passed. Both open decisions resolved —
  onboarding is tray + notifications only (no window); auto-start deferred to
  Phase 5. Human prerequisites: none. Spec accepted; PR #1 merged.
