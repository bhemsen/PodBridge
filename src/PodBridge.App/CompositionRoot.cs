using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Media;
using PodBridge.Windows;

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

        // Core telemetry pipeline (Phase 2). Both are singletons holding live event
        // subscriptions for the app's lifetime; the container disposes them
        // (IDisposable) on shutdown. The tracker gates the scanner's advertisements
        // on the Phase-1 IConnectionMonitor and publishes DeviceState; the engine
        // drives auto play/pause from the same gated in-ear transitions. The tracker
        // takes an optional TimeProvider that defaults to the system clock.
        services.AddSingleton<IDeviceStateProvider, DeviceStateTracker>();
        services.AddSingleton<AutoPlayPauseEngine>();

        // Phase 4 mic-profile policy engine (issue #30). A singleton holding live event
        // subscriptions to IAudioSessionMonitor for the app's lifetime (disposed by the
        // container on shutdown). Constructed at startup so it runs on the background
        // host and Auto-switch reacts live to comms-capture sessions; the tray drives
        // its mode + Call-mode toggle and reflects the single-device degrade warning.
        services.AddSingleton<MicPolicyEngine>();
    }
}
