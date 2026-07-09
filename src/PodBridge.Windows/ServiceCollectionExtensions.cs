using Microsoft.Extensions.DependencyInjection;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Media;

namespace PodBridge.Windows;

/// <summary>
/// Registration seam for the Windows adapters that implement
/// <c>PodBridge.Core</c>'s OS-boundary interfaces. The composition root in
/// <c>PodBridge.App</c> calls this so feature code never references concrete
/// adapters. Phase 1 registers the connection monitor; Phase 2 adds the BLE
/// advertisement scanner and the media-session controller; Phase 3 adds the
/// read-only audio-state reader.
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
        return services;
    }
}
