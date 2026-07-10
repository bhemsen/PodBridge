using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodBridge.Core.AdvancedTier;
using PodBridge.Core.Audio;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Branding;
using PodBridge.Core.Media;
using PodBridge.Core.Protocol;
using PodBridge.Core.Startup;

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
    private AboutWindow? _aboutWindow;
    private GestureSettingsWindow? _gestureSettingsWindow;
    private TrayStatusController? _trayStatusController;
    private TrayBatteryController? _trayBatteryController;
    private TrayAudioController? _trayAudioController;
    private TrayMicController? _trayMicController;
    private TrayNoiseControlController? _trayNoiseControlController;
    private IBleScanner? _bleScanner;
    private BleScannerSupervisor? _bleScannerSupervisor;
    private IBluetoothRadioSource? _radioSource;
    private MicPolicyEngine? _micPolicyEngine;
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

        // Tray icon is the app's primary surface; create it once the host is up and
        // tear it down on exit. The "About" entry opens the app's only window.
        _trayIcon = TrayIcon.Create();
        _trayIcon.SetAboutHandler(ShowAboutWindow);
        _trayIcon.SetGestureSettingsHandler(ShowGestureSettingsWindow);

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
        StartNoiseControlPipeline();
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

        // Phase-8 hardening (issue #55): drive scanning through the supervisor so a
        // Bluetooth-radio toggle restarts the watcher cleanly. Resolve + Start the
        // supervisor (it starts the scanner and subscribes to the radio source) BEFORE
        // starting the radio source, so no radio transition is missed.
        _bleScannerSupervisor = services.GetRequiredService<BleScannerSupervisor>();
        _bleScannerSupervisor.Start();

        _radioSource = services.GetRequiredService<IBluetoothRadioSource>();
        _radioSource.Start();
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
        _micPolicyEngine = engine;
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

    // Wires the Phase-6 Tier-2 noise-control submenu (issue #44). Resolves the Core
    // NoiseControlController (driving the opt-in IAapTransport) and the shared
    // IDeviceStateProvider (for the Adaptive model gate + connection-driven AAP session
    // start). With the advanced-tier driver absent — the Tier-1 default — the transport
    // reports IsAvailable == false, so the controller renders the submenu as disabled
    // with an honest explanation and never sends a frame or requests elevation.
    private void StartNoiseControlPipeline()
    {
        var services = _host!.Services;

        var controller = services.GetRequiredService<NoiseControlController>();
        var stateProvider = services.GetRequiredService<IDeviceStateProvider>();
        _trayNoiseControlController = TrayNoiseControlController.Create(
            _trayIcon!, controller, stateProvider, Dispatcher);
        _trayNoiseControlController.Start();

        // Phase-7 gesture-config re-push (issue #48). Resolve the singleton so it is
        // constructed and its subscription to the transport's (re)connect signal is live:
        // it then re-pushes the persisted GestureConfiguration on every Tier-2 (re)connect,
        // because Apple firmware forgets it on disconnect. The user-facing assignment surface
        // is the "Gesture controls…" window (issue #49), which persists via the same store
        // and delegates its write to this controller; with the driver absent it sends nothing.
        // The container owns and disposes it (unsubscribing) on shutdown.
        _ = services.GetRequiredService<GestureRepushController>();

        // The driver-absent "Enable advanced tier…" affordance: show the honest security
        // warning, then — only on explicit confirmation — launch the SEPARATE, ELEVATED
        // install step. The app stays asInvoker; it never auto-elevates or installs silently.
        _trayIcon!.SetEnableAdvancedTierHandler(EnableAdvancedTier);
    }

    // Explicit, user-triggered opt-in. Warns about BOTH machine-wide load requirements
    // (test-signing mode — which the user enables themselves; PodBridge never runs bcdedit —
    // and trusting the self-signed test cert) and the trade-off, then launches the elevated
    // installer (IAdvancedTierInstaller). When the driver package is absent (it ships
    // separately, never bundled), it guides the user to the advanced-tier docs instead.
    private void EnableAdvancedTier()
    {
        var proceed = MessageBox.Show(
            AdvancedTierInfo.SecurityWarning,
            AdvancedTierInfo.Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (proceed != MessageBoxResult.OK)
        {
            return; // opt-in: declining changes nothing.
        }

        var result = _host!.Services.GetRequiredService<IAdvancedTierInstaller>().Install();
        switch (result)
        {
            case AdvancedTierActionResult.Launched:
                _trayIcon!.ShowNotification(AdvancedTierInfo.Title, AdvancedTierInfo.LaunchedFollowUp);
                break;
            case AdvancedTierActionResult.PackageMissing:
                _trayIcon!.ShowNotification(AdvancedTierInfo.Title, AdvancedTierInfo.PackageMissingFollowUp);
                OpenAdvancedTierDocs();
                break;
            case AdvancedTierActionResult.Cancelled:
            default:
                break; // user declined the UAC prompt.
        }
    }

    // Opens the advanced-tier guide (the two load requirements + their trade-off) in the
    // browser. Shell-launch failures are non-fatal — the tray keeps running.
    private static void OpenAdvancedTierDocs()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProductInfo.AdvancedTierDocsUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                "Could not open the PodBridge advanced-tier documentation.",
                "PodBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // Opens the Tier-2 gesture-remap settings window from the tray "Gesture controls…"
    // entry. Binds the Core GestureSettingsController (availability decision + persistence +
    // write) and the shared IDeviceStateProvider (for the connected model); the driver-absent
    // panel reuses the same explicit, user-triggered EnableAdvancedTier flow as noise control
    // (no silent elevation). A single instance is reused, mirroring the About window; closing
    // it never exits the tray-resident app (ShutdownMode is OnExplicitShutdown).
    private void ShowGestureSettingsWindow()
    {
        if (_gestureSettingsWindow is not null)
        {
            _gestureSettingsWindow.Activate();
            return;
        }

        var services = _host!.Services;
        _gestureSettingsWindow = new GestureSettingsWindow(
            services.GetRequiredService<GestureSettingsController>(),
            services.GetRequiredService<IDeviceStateProvider>(),
            EnableAdvancedTier);
        _gestureSettingsWindow.Closed += (_, _) => _gestureSettingsWindow = null;
        _gestureSettingsWindow.Show();
    }

    // Opens the About window (the app's first non-tray window) from the tray "About"
    // entry. A single instance is reused: if it is already open, bring it to the
    // front rather than stacking duplicates. Runs on the UI dispatcher (the tray
    // handler fires on the UI thread). ShutdownMode is OnExplicitShutdown, so closing
    // this window never exits the tray-resident app.
    private void ShowAboutWindow()
    {
        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        // The auto-start toggle reads/sets the MSIX StartupTask via the Windows
        // adapter resolved from the composition root (default OFF); the branding is
        // the device-independent Core ProductInfo.
        var startupToggle = _host!.Services.GetRequiredService<IStartupToggle>();
        _aboutWindow = new AboutWindow(AboutViewModel.Create(), startupToggle);
        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
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

        // Unsubscribe the noise-control controller before its Core controller (a container
        // singleton) is disposed, so no late ModeChanged/StateChanged touches a disposed tray.
        _trayNoiseControlController?.Dispose();
        _trayNoiseControlController = null;

        _trayStatusController?.Dispose();
        _trayStatusController = null;

        // Stop the radio source first so no radio transition drives a restart during
        // teardown, then dispose the supervisor (unsubscribing it) and stop scanning so no
        // advertisement drives the pipeline. The container still disposes the scanner,
        // supervisor, and radio source (and the rest of the pipeline) below.
        _radioSource?.Stop();
        _radioSource = null;

        _bleScannerSupervisor?.Dispose();
        _bleScannerSupervisor = null;

        _bleScanner?.Stop();
        _bleScanner = null;

        // Graceful-shutdown restore (issue #55): return the default audio endpoints to the
        // user's prior routing so PodBridge leaves no rerouted devices behind. Runs before
        // the container disposes the engine; best-effort and never throws.
        _micPolicyEngine?.Restore();
        _micPolicyEngine = null;

        // Stop the comms-session monitor so no capture event drives the mic-policy
        // engine during teardown; the container still disposes the monitor and engine.
        _audioSessionMonitor?.Stop();
        _audioSessionMonitor = null;

        // Stop the device-topology change source so no endpoint event drives a Refresh
        // during teardown; the container still disposes it.
        _audioEndpointChangeMonitor?.Stop();
        _audioEndpointChangeMonitor = null;

        // Close the About window if the user left it open, so no window keeps the
        // process alive after an explicit shutdown.
        _aboutWindow?.Close();
        _aboutWindow = null;

        // Close the gesture-settings window too (it unsubscribes from device-state changes
        // on close), so no open window keeps the process alive after an explicit shutdown.
        _gestureSettingsWindow?.Close();
        _gestureSettingsWindow = null;

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
