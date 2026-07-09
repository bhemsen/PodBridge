using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Media;

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
    private TrayIcon? _trayIcon;
    private TrayStatusController? _trayStatusController;
    private TrayBatteryController? _trayBatteryController;
    private IBleScanner? _bleScanner;

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

        // Tray icon is the app's only surface in Phase 1; create it once the
        // host is up and tear it down on exit.
        _trayIcon = TrayIcon.Create();

        // Drive the tray from live connection status and show first-run pairing
        // guidance. StatusChanged is marshalled to this (UI) dispatcher inside
        // the controller.
        var monitor = _host.Services.GetRequiredService<IConnectionMonitor>();
        _trayStatusController = TrayStatusController.Create(
            _trayIcon, monitor, Dispatcher, new FirstRunGuidanceState());
        _trayStatusController.Start();

        StartTelemetryPipeline();
    }

    // Wires and starts the Phase-2 connection-gated Tier-1 pipeline
    // (scanner -> DeviceStateTracker -> tray battery + auto play/pause). The tracker
    // and engine subscribe to their sources in their constructors, so resolve them
    // (and the battery controller) BEFORE starting the scanner — then no
    // advertisement is missed. The container owns their lifetime; the tracker does
    // not own the scanner, so the composition root starts and stops it.
    private void StartTelemetryPipeline()
    {
        var services = _host!.Services;

        var stateProvider = services.GetRequiredService<IDeviceStateProvider>();

        // Resolve so the singleton is instantiated and its subscription to the
        // tracker is live; the container keeps it alive and disposes it on shutdown.
        _ = services.GetRequiredService<AutoPlayPauseEngine>();

        _trayBatteryController = TrayBatteryController.Create(_trayIcon!, stateProvider, Dispatcher);
        _trayBatteryController.Start();

        _bleScanner = services.GetRequiredService<IBleScanner>();
        _bleScanner.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unsubscribe the tray controllers first so no late StateChanged/StatusChanged
        // touches a disposed tray.
        _trayBatteryController?.Dispose();
        _trayBatteryController = null;

        _trayStatusController?.Dispose();
        _trayStatusController = null;

        // Stop scanning so no advertisement drives the pipeline during teardown; the
        // container still disposes the scanner (and the rest of the pipeline) below.
        _bleScanner?.Stop();
        _bleScanner = null;

        _trayIcon?.Dispose();
        _trayIcon = null;

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
