using Microsoft.Extensions.DependencyInjection;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Media;
using PodBridge.Core.Protocol;
using PodBridge.Core.Startup;

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

        // Device-topology change source (Core Audio IMMNotificationClient). A singleton
        // owning the enumerator + notification registration for the app's lifetime
        // (disposed by the container on shutdown). It triggers MicPolicyEngine.Refresh so
        // the single-device degrade warning updates live when the fallback mic is
        // added/removed; the composition root starts and stops it.
        services.AddSingleton<IAudioEndpointChangeMonitor, WindowsAudioEndpointChangeMonitor>();

        // Phase 5 opt-in auto-start-at-login (issue #35). The MSIX StartupTask toggle
        // is stateless — it fetches the task fresh per call and holds no handle
        // between calls (like the audio-state reader) — so it is transient. The About
        // surface resolves it on demand to read and set the default-OFF option.
        services.AddTransient<IStartupToggle, StartupTaskToggle>();

        // Phase 6 Tier-2 (advanced, opt-in) AAP transport over the optional KMDF driver
        // (issue #43). Registered unconditionally and safely: with the driver absent — the
        // Tier-1 default — it probes at construction, reports IsAvailable == false, and does
        // nothing, so Tier-1 is unaffected (constitution: graceful degradation). A singleton
        // because it owns a live device handle + background receive loop for the app's
        // lifetime; the container disposes it (IDisposable) on shutdown. It is the ONLY
        // component that talks to the driver.
        services.AddSingleton<IAapTransport, DriverAapTransport>();
        return services;
    }
}
