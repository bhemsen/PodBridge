# Architecture

> Structural, living document — the most volatile artifact. Update whenever a
> change alters components, boundaries, or flows. Greenfield seed, not a final
> design.

## Component map

| Component | Responsibility |
| --------- | -------------- |
| `PodBridge.Core` | Platform-neutral domain: AAP protocol module, Apple-Continuity BLE parser, device/battery/ear/noise-control models, mic-policy engine, and the OS-boundary interfaces (`IBleScanner`, `IAudioPolicy`, `IAudioSessionMonitor`, `IAapTransport`). No OS/UI/P-Invoke. |
| `PodBridge.Windows` | Windows adapters implementing the Core interfaces: `WinRtBleScanner` (BLE advertisement watcher), `WindowsAudioPolicy` (NAudio + `IPolicyConfig`), `WindowsAudioSessionMonitor` (`IAudioSessionManager2`), and — Tier 2 only — `DriverAapTransport` (talks to the KMDF driver). |
| `PodBridge.App` | WPF tray-first UI, view models, notifications, settings, and the composition root (DI, background host). |
| `driver/PodBridgeAAP` | Optional C / KMDF L2CAP-bridge driver exposing a user-mode device interface for AAP over PSM 0x1001. Ships separately. |
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
   receives a BLE advertisement → Core's Continuity parser decodes the Apple
   0x004C payload → updates `DeviceState` → `App` renders battery and, on an
   in-ear/out-of-ear transition, drives the Windows media session (pause/resume).
2. **Mic-profile policy (Tier 1):** `WindowsAudioSessionMonitor` detects a
   Communications capture session opening → Core's policy engine decides per the
   active mode (HiFi-lock / auto-switch / call-mode) → `WindowsAudioPolicy`
   sets the default vs communications endpoint per role → restores on release.
3. **Codec transparency (Tier 1):** on connect, `WindowsAudioPolicy` reads the
   negotiated codec / active mic mode → `App` surfaces AAC vs SBC and advice.
4. **Noise-control / gesture (Tier 2, opt-in):** `App` command → Core
   `AapProtocol` builds the packet → `DriverAapTransport` writes it over L2CAP via
   the driver → AirPods echo confirms → `DeviceState` updated. Driver absent →
   the feature is disabled in the UI.

## Where new code goes

- A new AAP command/telemetry → `PodBridge.Core/AapProtocol` (+ a transport call).
- A new OS capability (audio, BLE, media) → a `PodBridge.Windows` adapter behind
  a `Core` interface.
- New UI, tray, or notification behaviour → `PodBridge.App`.
- Device-independent logic and its tests → `PodBridge.Core` / `PodBridge.Core.Tests`.
- Anything needing the L2CAP channel (ANC, gestures) → the Tier-2 path
  (`DriverAapTransport` + driver), never the Tier-1 default.
