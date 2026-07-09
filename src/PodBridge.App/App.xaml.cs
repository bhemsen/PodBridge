using System.Windows;
using Microsoft.Extensions.Hosting;

namespace PodBridge.App;

/// <summary>
/// Application entry point. Enforces a single instance, starts the generic host
/// (the DI composition root), and runs tray-resident in the background with no
/// startup window (start-to-tray) until an explicit, clean shutdown.
/// </summary>
public partial class App : Application
{
    // Session-local: one instance per Windows user session (tray apps are per-user).
    private const string MutexName = @"Local\PodBridge.SingleInstance.4f3c9a02-2c1e-4d3a-9d0a-5f8a2b7c1d6e";

    private SingleInstanceGuard? _instanceGuard;
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceGuard = SingleInstanceGuard.Acquire(MutexName);
        if (!_instanceGuard.IsPrimaryInstance)
        {
            NotifyAlreadyRunning();
            Shutdown();
            return;
        }

        _host = CompositionRoot.BuildHost();
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }

        _instanceGuard?.Dispose();
        _instanceGuard = null;

        base.OnExit(e);
    }

    private static void NotifyAlreadyRunning()
        => MessageBox.Show(
            "PodBridge is already running.",
            "PodBridge",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
