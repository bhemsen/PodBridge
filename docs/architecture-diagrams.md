# Architecture diagrams

> Visual companion to [`architecture.md`](architecture.md). The prose in
> `architecture.md` is the source of truth; these diagrams mirror it at a glance
> and should be updated alongside it whenever components, boundaries, or flows
> change. Rendered natively by GitHub (Mermaid).

## 1. Component & layer map

Dependency direction and the Tier‑1 / Tier‑2 boundary. `PodBridge.Core` is
OS‑free; `PodBridge.Windows` implements Core's interfaces; `PodBridge.App` wires
the Windows implementations at the composition root only; the optional driver is
reached **only** through `DriverAapTransport`.

```mermaid
flowchart TB
    subgraph App["PodBridge.App — WPF tray UI + composition root"]
        AppUI["Tray controllers · About / Gesture windows · view models · notifications"]
        Root["Composition root — DI + background host"]
    end

    subgraph Core["PodBridge.Core — platform-neutral domain (no OS, no P/Invoke)"]
        Domain["Domain logic: AapProtocol · ContinuityParser · DeviceStateTracker · AutoPlayPauseEngine · MicPolicyEngine · AudioGuidanceEngine · ModelRegistry · CapabilityProvider · Diagnostics"]
        Ifaces["OS-boundary interfaces (abstractions)"]
    end

    subgraph Win["PodBridge.Windows — OS adapters (WinRT + P/Invoke, no UI)"]
        Adapters["WinRtBleScanner · WinRtConnectionMonitor · WinRtBluetoothRadioSource · WindowsMediaController · WindowsAudioStateReader · WindowsAudioPolicy · WindowsAudioSessionMonitor · RunKeyStartupToggle · DiagnosticsExporter"]
        T2Adapters["Tier 2: DriverAapTransport · AdvancedTierInstaller"]
    end

    subgraph Drv["driver/PodBridgeAAP — optional KMDF L2CAP driver (Tier 2, ships separately)"]
        Driver["AAP control channel over PSM 0x1001"]
    end

    AppUI --> Domain
    AppUI --> Ifaces
    Root -. wires impls, composition root only .-> Adapters
    Root -. wires .-> T2Adapters
    Adapters -- implement --> Ifaces
    T2Adapters -- implement --> Ifaces
    T2Adapters --> Driver

    classDef tier2 stroke-dasharray: 5 5;
    class Driver,T2Adapters tier2;
```

## 2. OS-boundary interface map

Every OS capability sits behind a `PodBridge.Core` interface, implemented by a
`PodBridge.Windows` adapter. Tier‑2 interfaces are only wired when the optional
driver path is used.

```mermaid
flowchart LR
    subgraph CoreIf["PodBridge.Core — interface"]
        I1["IBleScanner"]
        I2["IConnectionMonitor"]
        I3["IBluetoothRadioSource"]
        I4["IMediaController"]
        I5["IAudioStateReader"]
        I6["IAudioPolicy"]
        I7["IAudioSessionMonitor"]
        I8["IStartupToggle"]
        I9["IDiagnosticsExporter"]
        I10["IAapTransport — Tier 2"]
        I11["IAdvancedTierInstaller — Tier 2"]
    end
    subgraph WinImpl["PodBridge.Windows — implementation"]
        A1["WinRtBleScanner"]
        A2["WinRtConnectionMonitor"]
        A3["WinRtBluetoothRadioSource"]
        A4["WindowsMediaController"]
        A5["WindowsAudioStateReader"]
        A6["WindowsAudioPolicy"]
        A7["WindowsAudioSessionMonitor"]
        A8["RunKeyStartupToggle"]
        A9["DiagnosticsExporter"]
        A10["DriverAapTransport"]
        A11["AdvancedTierInstaller"]
    end
    I1 --- A1
    I2 --- A2
    I3 --- A3
    I4 --- A4
    I5 --- A5
    I6 --- A6
    I7 --- A7
    I8 --- A8
    I9 --- A9
    I10 --- A10
    I11 --- A11
```

## 3. Key flows

### 3.1 Battery + auto play/pause (Tier 1, driver-free)

```mermaid
sequenceDiagram
    autonumber
    participant BT as Bluetooth radio
    participant Scan as WinRtBleScanner (Windows)
    participant Track as DeviceStateTracker (Core)
    participant Conn as IConnectionMonitor (Core gate)
    participant Prov as IDeviceStateProvider (Core)
    participant Tray as TrayBatteryController (App)
    participant Auto as AutoPlayPauseEngine (Core)
    participant Media as WindowsMediaController (GSMTC)

    BT-->>Scan: Apple 0x004C BLE advertisement
    Scan->>Track: raw advertisement bytes
    Track->>Conn: check connection gate
    Conn-->>Track: Connected? else "unknown / out of range"
    Track->>Prov: publish DeviceState (battery, charging, in-ear)
    Prov-->>Tray: DeviceState changed
    Tray-->>Tray: render battery L / R / Case
    Prov-->>Auto: in-ear transition
    Auto->>Media: pause on first-bud-out, resume on both-in
```

### 3.2 Mic-profile policy (Tier 1)

```mermaid
sequenceDiagram
    autonumber
    participant Sess as WindowsAudioSessionMonitor (Windows)
    participant Eng as MicPolicyEngine (Core)
    participant Pol as WindowsAudioPolicy (IPolicyConfig)

    Sess->>Eng: Communications capture session opened
    Eng->>Eng: decide per mode (HiFi-lock / Auto-switch / Call-mode)
    Eng->>Pol: set default vs communications endpoint per role
    Note over Eng,Pol: snapshot prior routing before first apply; auto-rollback on mid-apply failure
    Sess->>Eng: session released
    Eng->>Pol: restore prior routing
```

### 3.3 Noise-control switching (Tier 2, opt-in driver)

```mermaid
sequenceDiagram
    autonumber
    participant UI as App (Noise control submenu)
    participant Ctl as NoiseControlController (Core)
    participant Proto as AapProtocol (Core)
    participant Trans as DriverAapTransport (Windows)
    participant Drv as KMDF driver
    participant Pods as AirPods

    UI->>Ctl: set mode (Off / ANC / Transparency / Adaptive)
    Ctl->>Ctl: optimistically apply mode
    Ctl->>Proto: build set-noise-control frame
    Proto-->>Ctl: AAP packet
    alt driver present
        Ctl->>Trans: write packet
        Trans->>Drv: L2CAP write (PSM 0x1001)
        Drv->>Pods: AAP control frame
        Pods-->>Drv: echo notification
        Drv-->>Trans: inbound frame
        Trans-->>Ctl: parsed echo
        Ctl->>Ctl: confirm, or revert on timeout / mismatch
    else driver absent
        Trans-->>Ctl: IsAvailable == false
        Ctl->>UI: feature disabled, offer "Enable advanced tier…"
    end
```

## 4. Noise-control state machine

The optimistic-set / echo-confirm / timeout-revert logic of
`NoiseControlController` over `IAapTransport`.

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> OptimisticallyApplied: user selects mode (UI updates at once)
    OptimisticallyApplied --> Confirmed: echo matches requested mode
    OptimisticallyApplied --> Reverted: timeout or echo mismatch
    Confirmed --> Idle
    Reverted --> Idle: restore previous mode in UI
    Idle --> Disabled: driver absent (IsAvailable == false)
    Disabled --> Idle: advanced tier enabled
```
