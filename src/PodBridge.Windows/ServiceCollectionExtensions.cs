using Microsoft.Extensions.DependencyInjection;

namespace PodBridge.Windows;

/// <summary>
/// Registration seam for the Windows adapters that implement
/// <c>PodBridge.Core</c>'s OS-boundary interfaces. The composition root in
/// <c>PodBridge.App</c> calls this so feature code never references concrete
/// adapters. Phase 2+ adapters (BLE scanner, audio policy, connection monitor)
/// are registered here; the Phase-1 skeleton establishes the composition seam
/// only.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds Core OS-boundary interfaces to their Windows implementations.
    /// </summary>
    public static IServiceCollection AddWindowsAdapters(this IServiceCollection services)
    {
        // No adapters yet: WinRt/audio adapters (e.g. WinRtBleScanner,
        // WindowsAudioPolicy) land here in later Phase-1/Phase-2 issues.
        return services;
    }
}
