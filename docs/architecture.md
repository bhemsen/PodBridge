# Architecture

> Structural, living document — the most volatile artifact. Update whenever a
> change alters components, boundaries, or flows. Greenfield seed, not a final
> design.

## Component map

| Component | Responsibility |
| --------- | -------------- |
| `PodBridge.Core` | Platform-neutral domain: the clean-room AAP module (`AapProtocol` frame builders + notification parsers) covering **noise-control** (`BuildSetNoiseControl`/`TryParseNoiseControlNotification` + `NoiseControlController` optimistic-set/echo-confirm/timeout-revert logic over `IAapTransport`, with `DeviceState.NoiseControl`/`NoiseControlAvailable`, `NoiseControlMode`, and the `NoiseControlSupport` Adaptive model gate) and **press-and-hold gesture remap** (`BuildSetPressAndHoldGesture`/`TryParsePressAndHoldGestureNotification` for the per-bud ClickHoldMode `0x16` frame — right=`data1`, left=`data2` — plus the `GestureAction` enum (Noise Control `0x01`/Siri `0x05` only; single/double presses are Apple-fixed and unexposed) and the per-bud `GestureConfiguration` model, the `GestureSupport` model gate (press-and-hold offered on the AirPods Pro 2 reference model only; broad matrix is Phase 8), the `GestureRepushController` re-push-on-reconnect policy, and the `GestureSettingsController`/`GestureAvailability` settings decision+apply logic that resolves the driver-absent/model-unsupported/available states and persists via `IGestureConfigStore` + writes via the re-push policy), Apple-Continuity BLE parser (`ContinuityParser` → `BleAdvertisement` in, `DeviceState` battery/charging/in-ear out), the Phase-8 model registry (`IModelRegistry`/`ModelRegistry`, backed by the clean-room `AppleModelIdentifier` per-model shape mapper) resolving an identified `AirPodsModel` to its `AirPodsModelInfo` shape (dual-bud vs single unit, battery-reporting case, in-ear vs head detection) for the vision's six supported models, with any other model — including Phase-2 `AirPodsModel` values outside that six-model scope — degrading to the labelled generic "Unknown AirPods" `AirPodsModelInfo` fallback (issue #52), the connection-gated telemetry pipeline (`IDeviceStateProvider`/`DeviceStateTracker`), the auto play/pause engine (`AutoPlayPauseEngine`), the read-only audio-state surface (`IAudioStateReader` → `AudioState`(`CodecKind`, `MicMode`) + `AudioGuidanceEngine`), mic-policy engine, the branding/disclaimer/license constants (`Branding/ProductInfo` — coined name, "for AirPods" descriptor, not-affiliated disclaimer, Apache-2.0 id, honest audio note, docs link; guarded by the Tier-1 `ProductInfoTests`), the opt-in auto-start toggle contract (`Startup/IStartupToggle` → `StartupToggleState`; default OFF, guarded by the Tier-1 `StartupToggleTests`), and the OS-boundary interfaces (`IBleScanner`, `IConnectionMonitor`, `IMediaController`, `IAudioStateReader`, `IAudioPolicy`, `IAudioSessionMonitor`, `IStartupToggle`, `IAapTransport`, and the advanced-tier opt-in install boundary `IAdvancedTierInstaller` → `AdvancedTierActionResult` plus the honest opt-in copy `AdvancedTier/AdvancedTierInfo`, guarded by `AdvancedTierInfoTests`). No OS/UI/P-Invoke. |
| `PodBridge.Windows` | Windows adapters implementing the Core interfaces: `WinRtBleScanner` (BLE advertisement watcher), `WinRtConnectionMonitor` (WinRT paired/connected detection), `WindowsMediaController` (GSMTC media-session pause/resume), `WindowsAudioStateReader` (read-only Core Audio codec + mic-mode read), `WindowsAudioPolicy` (NAudio + `IPolicyConfig`), `WindowsAudioSessionMonitor` (`IAudioSessionManager2`), `StartupTaskToggle` (opt-in auto-start over the MSIX `StartupTask` WinRT API, default OFF), and — Tier 2 only — `DriverAapTransport` (talks to the KMDF driver) and `AdvancedTierInstaller` (implements `IAdvancedTierInstaller`: locates the shipped-separately `install-advanced-tier.ps1` and launches it **elevated** via ShellExecute `runas` on explicit user action — driver `pnputil` install + self-signed test-cert trust in one step; never elevates the app, never runs `bcdedit`). |
| `PodBridge.App` | WPF tray-first UI, view models, notifications, settings, and the composition root (DI, background host). The tray context menu is driven by per-feature controllers (`TrayStatusController`, `TrayBatteryController`, `TrayAudioController`, `TrayMicController`, and — Tier 2, opt-in — `TrayNoiseControlController` for the "Noise control" submenu with the driver-absent honesty UX + Adaptive model gate). Includes the **About window** (`AboutWindow` + `AboutViewModel`) — the app's first non-tray window, opened from the tray "About" entry, which renders the Core `ProductInfo` branding/disclaimer/license/audio constants plus the running app version and the shipped `THIRD-PARTY-NOTICES.md`, and carries the **opt-in auto-start-at-login toggle** (default OFF) that reads/sets `IStartupToggle`. Adds the **gesture-controls window** (`GestureSettingsWindow`, opened from the tray "Gesture controls…" entry) — the Tier-2, opt-in press-and-hold remap surface: it binds the Core `GestureSettingsController` + `IDeviceStateProvider` to per-bud action pickers, and when the driver is absent (Tier-1 default) replaces them with the **reused** Phase-6 driver-absent notice (`TrayNoiseControlController.UnavailableText`, no new signed-driver claim) plus the same explicit "Enable advanced tier" flow — never silently broken. |
| `driver/PodBridgeAAP` | Optional C / KMDF L2CAP-bridge driver exposing a user-mode device interface for AAP over PSM 0x1001. Ships **separately** (never in the app MSIX): `build-testsign.ps1` builds + self-signs it, and `install-advanced-tier.ps1` is the elevated opt-in install/uninstall (`pnputil` + importing/removing the self-signed test cert in Trusted Root CA / Trusted Publishers). It never enables test-signing (`bcdedit`) — that stays a documented manual user step. |
| `packaging/PodBridge.Package` | Windows Application Packaging Project (`.wapproj`) that wraps `PodBridge.App` into a **signed MSIX** (app-only — no driver). Built by **MSBuild** on `windows-latest` (the `Package (MSIX)` workflow), deliberately kept **out of `PodBridge.slnx`** so the dotnet-based Verify gate is unaffected. Ships `Package.appxmanifest` (coined name "PodBridge", `runFullTrust`+`bluetooth` capabilities, `Windows.FullTrustApplication` = unelevated) and placeholder logos under `Images/`. |
| `tests/PodBridge.Core.Tests` | xUnit tests exercising Core via fakes — no physical device required. |
| `tests/PodBridge.Windows.Tests` | xUnit tests for the Windows adapters that have a device-independent seam — currently `DriverAapTransport` with a fake at the Win32 driver seam (connect/send/receive-loop/graceful-absence); no driver or hardware required. |

## Boundaries

- `PodBridge.Core` depends on nothing OS-specific; it is the only place with
  business logic and is fully unit-testable.
- `PodBridge.Windows` depends on `Core` (implements its interfaces) and may use
  WinRT / P-Invoke. No UI here.
- `PodBridge.App` depends on `Core` (abstractions) and wires in `Windows`
  implementations at the composition root only; feature code never references
  concrete adapters.
- The driver is reached **only** through `DriverAapTransport`; `App`/`Core` never
  talk to it directly. Everything Tier 1 runs with the driver absent.

## Key flows

1. **Battery + auto play/pause (Tier 1, driver-free):** `WinRtBleScanner`
   receives a BLE advertisement → `DeviceStateTracker` (Core) decodes the Apple
   `0x004C` proximity payload with `ContinuityParser`, applies the **Phase-1
   `IConnectionMonitor` connection gate**, a strongest-RSSI single-device
   heuristic, and a 30 s staleness timeout, then publishes `DeviceState` via
   `IDeviceStateProvider`. `App` renders the tray battery from it
   (`TrayBatteryController`), and `AutoPlayPauseEngine` (Core), on a
   both-in-ear→out transition, drives the current Windows media session via
   `WindowsMediaController` (`IMediaController`, GSMTC) — pausing on first-bud-out
   and resuming only media it paused. **Phase-2 telemetry and play/pause are gated
   on Phase-1's `IConnectionMonitor`**: with no AirPods connected (or the
   advertisement stale) the tray shows "unknown / out of range" and no play/pause
   fires — the company id `0x004C` identifies telemetry only and never enters the
   connection path. The composition root wires the pipeline and owns the scanner's
   start/stop; the tracker does not.
2. **Mic-profile policy (Tier 1):** `WindowsAudioSessionMonitor` detects a
   Communications capture session opening → Core's policy engine decides per the
   active mode (HiFi-lock / auto-switch / call-mode) → `WindowsAudioPolicy`
   sets the default vs communications endpoint per role → restores on release.
3. **Codec transparency (Tier 1, read-only):** on connect (and on a manual
   refresh), `WindowsAudioStateReader` (`IAudioStateReader`) reads an `AudioState`
   → Core's `AudioGuidanceEngine` maps it to honest display + advice text → `App`
   surfaces codec + mic-mode lines and an AAC-guidance notification only on
   confirmed SBC. This reader is **read-only** and deliberately separate from
   Phase 4's `IAudioPolicy` (which switches endpoints). Driver-free and
   admin-free: the negotiated codec is reported as `CodecKind.Unknown` because the
   only driver-free codec read (ETW `BthA2dp` provider) requires elevation, which
   Tier 1's `asInvoker` forbids (docs/research/codec-detection.md); the mic mode is
   inferred read-only from an active capture session on the AirPods mic endpoint
   via `IAudioSessionManager2`, never opening a stream on the mic
   (docs/research/mic-mode-detection.md).
4. **Noise-control / gesture (Tier 2, opt-in):** `App` command →
   `NoiseControlController` optimistically applies the mode and asks `AapProtocol`
   to build the packet → `DriverAapTransport` writes it over L2CAP via the driver →
   the AirPods echo notification is parsed by `AapProtocol` and confirms (or a
   timeout/mismatch reverts) → `DeviceState` updated. Driver absent → the transport
   reports `IsAvailable == false`, `ApplyTo` disables the feature in the UI, and no
   packet is sent. **Implemented:** the Core logic (`AapProtocol`,
   `NoiseControlController`, `IAapTransport`; issue #41, unit-tested via a fake
   transport), the KMDF driver (`driver/PodBridgeAAP`), and — issue #43 —
   `DriverAapTransport` (`PodBridge.Windows`), which opens the driver's device
   interface, issues the connect/send IOCTLs, and runs a background receive loop over
   the inverted-call receive IOCTL; it probes at startup and reports
   `IsAvailable == false` when the driver is absent (device-independent tests fake the
   Win32 seam). Issue #44 adds the tray "Noise control" submenu: `TrayNoiseControlController`
   (`PodBridge.App`) binds the Core `NoiseControlController` + `IDeviceStateProvider` to
   the submenu, reflects the optimistic mode, raises a transient toast on timeout-revert,
   gates Adaptive on the connected model via Core `NoiseControlSupport` (Pro 2 reference),
   and — with the driver absent — disables the modes and shows an honest explanation plus
   an "Enable advanced tier…" affordance rather than failing silently; the app stays
   `asInvoker`. Issue #45 wires that affordance to the opt-in install experience: the App
   shows the honest security warning (`AdvancedTier/AdvancedTierInfo`, stating **both** x64
   load requirements — test-signing mode, which the user enables themselves, **and** the
   self-signed test-cert trust — and their trade-off), then on explicit confirmation calls
   `IAdvancedTierInstaller` (`AdvancedTierInstaller`), which launches the separate
   `install-advanced-tier.ps1` **elevated** (`pnputil` install + cert trust in one step);
   the app never elevates itself and never runs `bcdedit`. When the driver package isn't
   present locally the installer reports `PackageMissing` and the App opens the advanced-tier
   guide. This flow is now implemented end-to-end (the real elevated install + test-signing +
   functional behaviour on real hardware remain the Tier-2 manual/human smoke test).
   **Gesture remap (Phase 7):** the same Tier-2 path carries the press-and-hold remap —
   `AapProtocol.BuildSetPressAndHoldGesture` builds the per-bud ClickHoldMode `0x16` frame
   (Noise Control / Siri, right=`data1`/left=`data2`) which `DriverAapTransport` writes over
   L2CAP, and the echo is parsed by `AapProtocol.TryParsePressAndHoldGestureNotification`
   (issue #47, unit-tested via a fake transport). Because Apple firmware overwrites the
   config on reconnect, issue #48 adds a Tier-2 **(re)connect signal to the Core
   `IAapTransport` interface** — `event EventHandler? Connected`, raised by
   `DriverAapTransport` each time `ConnectAsync` opens a fresh channel (not on the
   idempotent already-open return), and firable by a fake transport in tests; no change
   to the write path or handshake logic. The Core `GestureRepushController` subscribes to
   that signal and, on every (re)connect, reloads the persisted `GestureConfiguration` from
   the Core `IGestureConfigStore` abstraction (Windows adapter `GestureConfigStore`, a
   per-user file under `%LOCALAPPDATA%\PodBridge`) and re-writes it, confirming with the
   Phase-6 write+echo pattern and a **single** retry — a missing echo is a non-fatal
   "couldn't apply" (no retry storm). It is resolved on the background host so the
   subscription is live; with the driver absent the transport reports `IsAvailable == false`
   and nothing is sent (graceful degradation). Issue #49 adds the gesture **settings UI**:
   the Core `GestureSettingsController` resolves availability (driver-absent /
   model-unsupported / available) via the `GestureSupport` model gate (AirPods Pro 2
   reference model; broad matrix is Phase 8), exposes the honest action set (Noise Control /
   Siri), and on apply persists the per-bud `GestureConfiguration` via `IGestureConfigStore`
   then writes it by delegating to `GestureRepushController` (one shared write+echo-confirm
   path for the immediate apply and the reconnect re-push). `App`'s `GestureSettingsWindow`
   (opened from the tray "Gesture controls…" entry) binds that controller +
   `IDeviceStateProvider` to per-bud pickers; with the driver absent it hides the pickers,
   shows the reused Phase-6 driver-absent notice + the explicit "Enable advanced tier" flow,
   and attempts no packet (graceful-degradation gate). Applying on real hardware and the
   re-push after a physical reconnect remain the Tier-2 human QA smoke test.
5. **Connection detection (Tier 1, driver-free):** `WinRtConnectionMonitor`
   watches paired Bluetooth-Classic association endpoints and holds a
   `BluetoothDevice` per matched AirPods (name heuristic) for its
   `ConnectionStatusChanged` edge → maps to Core's `ConnectionStatus`
   (`Connected` / `Disconnected` / `NoDevice` / `BluetoothUnavailable`) behind
   `IConnectionMonitor` → `App` renders the tray status and pairing guidance. No
   Bluetooth radio → `BluetoothUnavailable`, never a crash.

## Where new code goes

- A new AAP command/telemetry → `PodBridge.Core/AapProtocol` (+ a transport call).
- A new OS capability (audio, BLE, media) → a `PodBridge.Windows` adapter behind
  a `Core` interface.
- New UI, tray, or notification behaviour → `PodBridge.App`.
- Device-independent logic and its tests → `PodBridge.Core` / `PodBridge.Core.Tests`.
- Anything needing the L2CAP channel (ANC, gestures) → the Tier-2 path
  (`DriverAapTransport` + driver), never the Tier-1 default.
- Advanced-tier install/signing UX → the honest copy + boundary in Core
  (`AdvancedTier/`), the elevated launch in `PodBridge.Windows`
  (`AdvancedTierInstaller`), and the driver's own `install-advanced-tier.ps1`;
  never bundle the driver in the app MSIX and never auto-elevate the app.
- Packaging/distribution (MSIX manifest, capabilities, logos) →
  `packaging/PodBridge.Package` (the `.wapproj`); built by MSBuild in the
  `Package (MSIX)` workflow, never added to `PodBridge.slnx`.
