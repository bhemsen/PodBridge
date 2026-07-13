using Microsoft.Extensions.DependencyInjection;
using PodBridge.Core.AdvancedTier;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Media;
using PodBridge.Core.Protocol;
using PodBridge.Core.Startup;
using PodBridge.Windows.Logging;

namespace PodBridge.Windows;

/// <summary>
/// Registration seam for the Windows adapters that implement
/// <c>PodBridge.Core</c>'s OS-boundary interfaces. The composition root in
/// <c>PodBridge.App</c> calls this so feature code never references concrete
/// adapters. Phase 1 registers the connection monitor; Phase 2 adds the BLE
/// advertisement scanner and the media-session controller; Phase 3 adds the
/// read-only audio-state reader; Phase 4 adds the mic-profile policy lever
/// (<see cref="IAudioPolicy"/>) and the comms-capture session monitor
/// (<see cref="IAudioSessionMonitor"/>); Phase 5 adds the opt-in auto-start toggle
/// (<see cref="IStartupToggle"/>).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds Core OS-boundary interfaces to their Windows implementations. The
    /// subscription-holding adapters are singletons: they hold live WinRT handles /
    /// subscriptions that must survive for the app's lifetime, and the container
    /// disposes them (IDisposable) on host shutdown. Stateless, per-call adapters
    /// (the audio-state reader) are transient.
    /// </summary>
    public static IServiceCollection AddWindowsAdapters(this IServiceCollection services)
    {
        // The monitor holds live BluetoothDevice references whose
        // ConnectionStatusChanged events stop firing once garbage-collected, so
        // exactly one instance must live for the app's lifetime.
        services.AddSingleton<IConnectionMonitor, WinRtConnectionMonitor>();

        // The scanner owns a BluetoothLEAdvertisementWatcher + its Received handler;
        // the controller owns the GSMTC session manager. One instance each.
        services.AddSingleton<IBleScanner, WinRtBleScanner>();
        services.AddSingleton<IMediaController, WindowsMediaController>();

        // Phase-8 hardening (issue #55): the Bluetooth-radio power-state source that lets
        // BleScannerSupervisor restart the driver-free watcher cleanly on a radio toggle.
        // A singleton owning a live Radio.StateChanged subscription for the app's lifetime
        // (disposed by the container on shutdown); it degrades to "assume on" if radio
        // enumeration faults, so Tier-1 scanning never regresses.
        services.AddSingleton<IBluetoothRadioSource, WinRtBluetoothRadioSource>();

        // The audio-state reader holds no subscription or handle between calls — it
        // creates and releases short-lived Core Audio COM objects per Read() — so it
        // needs no shared lifetime and is registered transient (no IDisposable).
        services.AddTransient<IAudioStateReader, WindowsAudioStateReader>();

        // Phase 4 mic-profile policy (issue #30). Both are singletons: the session
        // monitor owns a background MTA COM thread + IAudioSessionManager2 notification
        // registrations that must live for the app's lifetime, and the container
        // disposes it (IDisposable) on shutdown. The audio-policy adapter is stateless
        // per call (it creates + releases short-lived IPolicyConfig/MMDevice COM objects
        // each call) but its sole consumer is the singleton MicPolicyEngine, so a single
        // shared instance keeps the lifetime unambiguous (no captive-transient).
        services.AddSingleton<IAudioSessionMonitor, WindowsAudioSessionMonitor>();
        services.AddSingleton<IAudioPolicy, WindowsAudioPolicy>();

        // Issue #156: the comms-profile engager that forces the AirPods HFP link up via a
        // silent Communications-category render keep-alive, so the AirPods capture (mic)
        // endpoint comes live when the mic policy promotes them to the comms role — a
        // routing-role set alone never wakes HFP. A singleton because it owns a live WASAPI
        // render stream for as long as the AirPods hold comms; the container disposes it
        // (IDisposable) on shutdown, releasing the stream. Render-only, no capture stream;
        // any COM failure degrades to a no-op (constitution: graceful degradation).
        services.AddSingleton<ICommsProfileEngager, WindowsCommsProfileEngager>();

        // Device-topology change source (Core Audio IMMNotificationClient). A singleton
        // owning the enumerator + notification registration for the app's lifetime
        // (disposed by the container on shutdown). It triggers MicPolicyEngine.Refresh so
        // the single-device degrade warning updates live when the fallback mic is
        // added/removed; the composition root starts and stops it.
        services.AddSingleton<IAudioEndpointChangeMonitor, WindowsAudioEndpointChangeMonitor>();

        // Phase 5 opt-in auto-start-at-login (issue #35), replaced by the portable HKCU
        // Run-key adapter (issue #117) now the app ships as a self-contained exe with no
        // MSIX package identity. It is stateless — it opens the registry fresh per call and
        // holds no handle between calls (like the audio-state reader) — so it is transient.
        // The About surface resolves it on demand to read and set the default-OFF option.
        services.AddTransient<IStartupToggle, RunKeyStartupToggle>();

        // Phase 6 Tier-2 (advanced, opt-in) AAP transport over the optional KMDF driver
        // (issue #43). Registered unconditionally and safely: with the driver absent — the
        // Tier-1 default — it probes at construction, reports IsAvailable == false, and does
        // nothing, so Tier-1 is unaffected (constitution: graceful degradation). A singleton
        // because it owns a live device handle + background receive loop for the app's
        // lifetime; the container disposes it (IDisposable) on shutdown. It is the ONLY
        // component that talks to the driver.
        services.AddSingleton<IAapTransport, DriverAapTransport>();

        // Phase 7 gesture-config persistence (issue #48). The store is a small per-user
        // file under %LOCALAPPDATA%\PodBridge (local-only, no network); it holds no handle
        // between calls, so it is registered as a singleton only because its sole consumer
        // (the singleton GestureRepushController) reloads it on every Tier-2 (re)connect.
        // Core stays OS-free — it depends on the IGestureConfigStore abstraction, this is
        // the Windows file-backed adapter.
        services.AddSingleton<IGestureConfigStore, GestureConfigStore>();

        // Phase 6 advanced-tier opt-in install step (issue #45). Stateless — it holds no
        // handle between calls (it locates the separate install script and launches it
        // elevated on an explicit user action) — so it is transient, like the other
        // per-call adapters. It NEVER elevates the app (asInvoker) and NEVER runs bcdedit;
        // it is invoked only from the App's explicit "Enable advanced tier" affordance.
        services.AddTransient<IAdvancedTierInstaller, AdvancedTierInstaller>();

        // Phase 8 local diagnostics export (issue #54). Stateless — it holds no handle
        // between calls (it renders the snapshot and writes one timestamped file per
        // export) — so it is transient, like the other per-call adapters. It touches only
        // the local filesystem (constitution: local-only, no network sink).
        services.AddTransient<IDiagnosticsExporter, DiagnosticsExporter>();

        // Phase 8 structured local logging (issue #54). The rolling, size/age-capped local
        // file sink (~10 MB / 7 days) is a Windows filesystem adapter. Registered as a
        // singleton so the composition root can add the SAME instance as the ONLY
        // ILoggerProvider AND the tray "Debug logging" toggle can flip its MinLevel at
        // runtime — one instance, local file only, no network sink (constitution: local-only).
        services.AddSingleton<RollingFileLoggerProvider>();
        return services;
    }
}
