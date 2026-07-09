using Microsoft.Extensions.DependencyInjection;
using PodBridge.Core.Bluetooth;

namespace PodBridge.Windows;

/// <summary>
/// Registration seam for the Windows adapters that implement
/// <c>PodBridge.Core</c>'s OS-boundary interfaces. The composition root in
/// <c>PodBridge.App</c> calls this so feature code never references concrete
/// adapters. Phase 2+ adapters (BLE scanner, audio policy) land here too;
/// Phase 1 registers the connection monitor.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds Core OS-boundary interfaces to their Windows implementations.
    /// </summary>
    public static IServiceCollection AddWindowsAdapters(this IServiceCollection services)
    {
        // Singleton: the monitor holds live BluetoothDevice references whose
        // ConnectionStatusChanged events stop firing once garbage-collected, so
        // exactly one instance must live for the app's lifetime. The container
        // disposes it (IDisposable) on host shutdown.
        services.AddSingleton<IConnectionMonitor, WinRtConnectionMonitor>();
        return services;
    }
}
