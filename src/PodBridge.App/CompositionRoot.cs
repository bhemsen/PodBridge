using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Capabilities;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Media;
using PodBridge.Core.Protocol;
using PodBridge.Windows;
using PodBridge.Windows.Logging;

namespace PodBridge.App;

/// <summary>
/// Builds the application's generic host and its dependency-injection
/// composition root. This is the single place that binds <c>PodBridge.Core</c>
/// abstractions to their <c>PodBridge.Windows</c> adapters; feature code
/// resolves interfaces and never references concrete adapters.
/// </summary>
public static class CompositionRoot
{
    /// <summary>Creates the configured, not-yet-started generic host.</summary>
    public static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        ConfigureServices(builder.Services);
        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core <-> Windows seam: Windows adapters register themselves here.
        services.AddWindowsAdapters();

        // Phase 8 structured local logging (issue #54): replace the generic host's default
        // providers (console/debug/eventsource/eventlog) with our SINGLE rolling local-file
        // sink — there is no console and, above all, no network sink. The same
        // RollingFileLoggerProvider singleton (registered by AddWindowsAdapters) backs the
        // tray "Debug logging" toggle (a runtime MinLevel flip), so verbosity is raised to
        // the local file only (constitution: local-only, no telemetry).
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.Services.AddSingleton<ILoggerProvider>(
                sp => sp.GetRequiredService<RollingFileLoggerProvider>());
        });

        // Phase 8 diagnostics + capability model (issues #52/#53/#54). The Core model
        // registry and capability provider are OS-free, stateless singletons; #54 is the
        // first real consumer of ICapabilityProvider. The BLE-parse-history recorder is a
        // Core type that subscribes to the raw IBleScanner (like DeviceStateTracker) —
        // a singleton the container disposes (unsubscribing) on shutdown. The snapshot
        // factory assembles the current facts on demand for the tray "Export diagnostics"
        // action; it holds no subscription, so it is transient.
        services.AddSingleton<IModelRegistry, ModelRegistry>();
        services.AddSingleton<ICapabilityProvider, CapabilityProvider>();
        services.AddSingleton<IBleParseHistory, BleParseHistoryRecorder>();
        services.AddTransient<IDiagnosticsSnapshotFactory, DiagnosticsSnapshotFactory>();

        // Core telemetry pipeline (Phase 2). Both are singletons holding live event
        // subscriptions for the app's lifetime; the container disposes them
        // (IDisposable) on shutdown. The tracker gates the scanner's advertisements
        // on the Phase-1 IConnectionMonitor and publishes DeviceState; the engine
        // drives auto play/pause from the same gated in-ear transitions. The tracker
        // takes an optional TimeProvider that defaults to the system clock.
        services.AddSingleton<IDeviceStateProvider, DeviceStateTracker>();
        services.AddSingleton<AutoPlayPauseEngine>();

        // Phase-8 hardening (issue #55). Owns the BLE-watcher lifecycle relative to the
        // Bluetooth-radio power state: it starts scanning unconditionally (no Tier-1
        // regression) and, on a radio off→on toggle, restarts the scanner with a fresh
        // watcher (the WinRT watcher does not resume scanning by itself after the radio
        // was off). Core type driving only IBleScanner + IBluetoothRadioSource; the
        // container disposes it (IDisposable) on shutdown, unsubscribing it.
        services.AddSingleton<BleScannerSupervisor>();

        // Phase 4 mic-profile policy engine (issue #30). A singleton holding live event
        // subscriptions to IAudioSessionMonitor for the app's lifetime (disposed by the
        // container on shutdown). Constructed at startup so it runs on the background
        // host and Auto-switch reacts live to comms-capture sessions; the tray drives
        // its mode + Call-mode toggle and reflects the single-device degrade warning.
        services.AddSingleton<MicPolicyEngine>();

        // Issue #173: Windows audio-stack-collapse detection. A singleton subscribing to
        // the same IAudioEndpointChangeMonitor for the app's lifetime (disposed by the
        // container on shutdown), so it and MicPolicyEngine each see every topology change
        // independently. Core logic only — the OS signal comes from the Windows adapter,
        // the tray notification + recovery window are TrayAudioCollapseController's job.
        services.AddSingleton<AudioCollapseDetector>();

        // Phase 6 Tier-2 noise-control state machine (issue #44). Core logic driving the
        // opt-in IAapTransport with the optimistic-set / echo-confirm / timeout-revert
        // model; the tray (TrayNoiseControlController) is its only consumer. A singleton
        // holding the current confirmed mode for the app's lifetime. Built via a factory
        // so the transport is bound explicitly and the confirm-timeout / clock defaults
        // apply; with the driver absent it reports IsAvailable == false and sends nothing
        // (constitution: graceful degradation).
        services.AddSingleton(sp => new NoiseControlController(sp.GetRequiredService<IAapTransport>()));

        // Phase 7 gesture-config re-push policy (issue #48). A singleton that subscribes to
        // the IAapTransport (re)connect signal in its constructor and re-pushes the persisted
        // GestureConfiguration on every Tier-2 (re)connect (Apple firmware forgets it on
        // disconnect). Built via a factory so the transport + IGestureConfigStore are bound
        // explicitly and the confirm-timeout / clock defaults apply; with the driver absent
        // the transport reports IsAvailable == false and it sends nothing (graceful
        // degradation). The container disposes it (IDisposable) on shutdown, unsubscribing it.
        services.AddSingleton(sp => new GestureRepushController(
            sp.GetRequiredService<IAapTransport>(),
            sp.GetRequiredService<IGestureConfigStore>()));

        // Phase 7 gesture-remap settings surface (issue #49). The Core decision + apply logic
        // behind the gesture settings window: it resolves availability (driver present +
        // supported model), exposes the honest action set, persists the user's assignment via
        // IGestureConfigStore, and writes it by delegating to the GestureRepushController above
        // (one shared write+echo-confirm path for the immediate apply and the reconnect
        // re-push). A singleton bound via a factory so the transport + store + re-push policy
        // are wired explicitly; App feature code depends only on these Core abstractions and
        // never on the concrete driver adapter. With the driver absent it reports
        // DriverUnavailable and writes nothing (graceful degradation).
        services.AddSingleton(sp => new GestureSettingsController(
            sp.GetRequiredService<IAapTransport>(),
            sp.GetRequiredService<IGestureConfigStore>(),
            sp.GetRequiredService<GestureRepushController>()));
    }
}
