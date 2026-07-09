using Microsoft.Extensions.DependencyInjection;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Media;

namespace PodBridge.Windows;

/// <summary>
/// Registration seam for the Windows adapters that implement
/// <c>PodBridge.Core</c>'s OS-boundary interfaces. The composition root in
/// <c>PodBridge.App</c> calls this so feature code never references concrete
/// adapters. Phase 1 registers the connection monitor; Phase 2 adds the BLE
/// advertisement scanner and the media-session controller.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds Core OS-boundary interfaces to their Windows implementations. All are
    /// singletons: they hold live WinRT handles / subscriptions that must survive
    /// for the app's lifetime, and the container disposes them (IDisposable) on host
    /// shutdown.
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
        return services;
    }
}
