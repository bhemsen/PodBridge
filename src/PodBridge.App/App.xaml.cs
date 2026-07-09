using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodBridge.Core.Audio;
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
    private TrayAudioController? _trayAudioController;
    private TrayMicController? _trayMicController;
    private IBleScanner? _bleScanner;
    private IAudioSessionMonitor? _audioSessionMonitor;
    private IAudioEndpointChangeMonitor? _audioEndpointChangeMonitor;

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

        var monitor = _host.Services.GetRequiredService<IConnectionMonitor>();

        // Surface the read-only audio state (codec + mic-mode lines, "Refresh audio
        // status", and the confirmed-SBC guidance notification). Start it BEFORE the
        // status controller (which starts the monitor) so its StatusChanged
        // subscription is live for the initial connect and no on-connect read is
        // missed. Reads happen on connect and on manual refresh only — no polling.
        var audioReader = _host.Services.GetRequiredService<IAudioStateReader>();
        _trayAudioController = TrayAudioController.Create(
            _trayIcon, audioReader, monitor, Dispatcher);
        _trayAudioController.Start();

        // Drive the tray from live connection status and show first-run pairing
        // guidance. StatusChanged is marshalled to this (UI) dispatcher inside
        // the controller.
        _trayStatusController = TrayStatusController.Create(
            _trayIcon, monitor, Dispatcher, new FirstRunGuidanceState());
        _trayStatusController.Start();

        StartTelemetryPipeline();
        StartMicPolicyPipeline();
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

    // Wires and starts the Phase-4 mic-profile policy on the background host so
    // Auto-switch reacts live to comms-capture sessions. Resolve the engine FIRST so its
    // subscription to the IAudioSessionMonitor is live before the monitor starts (no
    // comms event missed); the tray controller then restores the persisted mode and
    // reflects the degrade warning. The container owns the engine's and monitor's
    // lifetime; the composition root starts and stops the monitor (the engine, like the
    // tracker with the scanner, does not own it).
    private void StartMicPolicyPipeline()
    {
        var services = _host!.Services;

        var engine = services.GetRequiredService<MicPolicyEngine>();
        _trayMicController = TrayMicController.Create(
            _trayIcon!, engine, new MicPolicyModeStore(), Dispatcher);
        _trayMicController.Start();

        _audioSessionMonitor = services.GetRequiredService<IAudioSessionMonitor>();
        _audioSessionMonitor.Start();

        // Start the device-topology change source AFTER the engine is resolved (its
        // EndpointsChanged subscription is wired in the engine's constructor), so a
        // fallback-mic add/remove drives a live Refresh of the degrade warning. The
        // container owns its lifetime; the composition root starts and stops it.
        _audioEndpointChangeMonitor = services.GetRequiredService<IAudioEndpointChangeMonitor>();
        _audioEndpointChangeMonitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unsubscribe the tray controllers first so no late StateChanged/StatusChanged
        // touches a disposed tray.
        _trayBatteryController?.Dispose();
        _trayBatteryController = null;

        _trayAudioController?.Dispose();
        _trayAudioController = null;

        // Unsubscribe the mic-policy controller before its engine (a container singleton)
        // is disposed, so no late warning event touches a disposed tray.
        _trayMicController?.Dispose();
        _trayMicController = null;

        _trayStatusController?.Dispose();
        _trayStatusController = null;

        // Stop scanning so no advertisement drives the pipeline during teardown; the
        // container still disposes the scanner (and the rest of the pipeline) below.
        _bleScanner?.Stop();
        _bleScanner = null;

        // Stop the comms-session monitor so no capture event drives the mic-policy
        // engine during teardown; the container still disposes the monitor and engine.
        _audioSessionMonitor?.Stop();
        _audioSessionMonitor = null;

        // Stop the device-topology change source so no endpoint event drives a Refresh
        // during teardown; the container still disposes it.
        _audioEndpointChangeMonitor?.Stop();
        _audioEndpointChangeMonitor = null;

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
