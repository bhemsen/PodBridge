using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    }
}
