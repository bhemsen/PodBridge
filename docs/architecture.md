# Architecture

> Structural, living document — the most volatile artifact. Update whenever a
> change alters components, boundaries, or flows. Greenfield seed, not a final
> design.

## Component map

| Component | Responsibility |
| --------- | -------------- |
| `PodBridge.Core` | Platform-neutral domain: AAP protocol module, Apple-Continuity BLE parser (`ContinuityParser` → `BleAdvertisement` in, `DeviceState` battery/charging/in-ear out), the connection-gated telemetry pipeline (`IDeviceStateProvider`/`DeviceStateTracker`), the auto play/pause engine (`AutoPlayPauseEngine`), the read-only audio-state surface (`IAudioStateReader` → `AudioState`(`CodecKind`, `MicMode`) + `AudioGuidanceEngine`), noise-control models, mic-policy engine, and the OS-boundary interfaces (`IBleScanner`, `IConnectionMonitor`, `IMediaController`, `IAudioStateReader`, `IAudioPolicy`, `IAudioSessionMonitor`, `IAapTransport`). No OS/UI/P-Invoke. |
| `PodBridge.Windows` | Windows adapters implementing the Core interfaces: `WinRtBleScanner` (BLE advertisement watcher), `WinRtConnectionMonitor` (WinRT paired/connected detection), `WindowsMediaController` (GSMTC media-session pause/resume), `WindowsAudioStateReader` (read-only Core Audio codec + mic-mode read), `WindowsAudioPolicy` (NAudio + `IPolicyConfig`), `WindowsAudioSessionMonitor` (`IAudioSessionManager2`), and — Tier 2 only — `DriverAapTransport` (talks to the KMDF driver). |
| `PodBridge.App` | WPF tray-first UI, view models, notifications, settings, and the composition root (DI, background host). |
| `driver/PodBridgeAAP` | Optional C / KMDF L2CAP-bridge driver exposing a user-mode device interface for AAP over PSM 0x1001. Ships separately. |
| `packaging/PodBridge.Package` | Windows Application Packaging Project (`.wapproj`) that wraps `PodBridge.App` into a **signed MSIX** (app-only — no driver). Built by **MSBuild** on `windows-latest` (the `Package (MSIX)` workflow), deliberately kept **out of `PodBridge.slnx`** so the dotnet-based Verify gate is unaffected. Ships `Package.appxmanifest` (coined name "PodBridge", `runFullTrust`+`bluetooth` capabilities, `Windows.FullTrustApplication` = unelevated) and placeholder logos under `Images/`. |
| `tests/PodBridge.Core.Tests` | xUnit tests exercising Core via fakes — no physical device required. |

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
4. **Noise-control / gesture (Tier 2, opt-in):** `App` command → Core
   `AapProtocol` builds the packet → `DriverAapTransport` writes it over L2CAP via
   the driver → AirPods echo confirms → `DeviceState` updated. Driver absent →
   the feature is disabled in the UI.
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
- Packaging/distribution (MSIX manifest, capabilities, logos) →
  `packaging/PodBridge.Package` (the `.wapproj`); built by MSBuild in the
  `Package (MSIX)` workflow, never added to `PodBridge.slnx`.
