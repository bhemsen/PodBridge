using System.Windows.Threading;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.App;

/// <summary>
/// Wires an <see cref="IConnectionMonitor"/> to the <see cref="TrayIcon"/>:
/// renders the initial status, updates the tray status line/tooltip live on every
/// connect/disconnect (marshalled to the WPF UI thread, since the monitor raises
/// its event from a WinRT/thread-pool thread), and shows a one-time first-run
/// pairing-guidance notification when no AirPods are paired. Owns only the event
/// subscription; the monitor's lifetime belongs to the DI container.
/// </summary>
public sealed class TrayStatusController : IDisposable
{
    private const string GuidanceTitle = "Pair your AirPods";

    private const string GuidanceMessage =
        "No AirPods are paired yet. Use \"Pair / Reconnect\" to add them in Windows Bluetooth settings.";

    private readonly TrayIcon _tray;
    private readonly IConnectionMonitor _monitor;
    private readonly Dispatcher _dispatcher;
    private readonly FirstRunGuidanceState _firstRun;

    private TrayStatusController(
        TrayIcon tray,
        IConnectionMonitor monitor,
        Dispatcher dispatcher,
        FirstRunGuidanceState firstRun)
    {
        _tray = tray;
        _monitor = monitor;
        _dispatcher = dispatcher;
        _firstRun = firstRun;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="monitor"/> to
    /// <paramref name="tray"/>. Call <see cref="Start"/> to begin monitoring.
    /// </summary>
    public static TrayStatusController Create(
        TrayIcon tray,
        IConnectionMonitor monitor,
        Dispatcher dispatcher,
        FirstRunGuidanceState firstRun)
        => new(tray, monitor, dispatcher, firstRun);

    /// <summary>
    /// Subscribes to status changes, renders the current status, and starts the
    /// monitor. Must be called on the UI thread (e.g. from app startup).
    /// </summary>
    public void Start()
    {
        _monitor.StatusChanged += OnStatusChanged;
        Apply(_monitor.CurrentStatus);
        _ = StartMonitorAsync();
    }

    /// <summary>Unsubscribes so no late event touches a disposed tray.</summary>
    public void Dispose() => _monitor.StatusChanged -= OnStatusChanged;

    private async Task StartMonitorAsync()
    {
        try
        {
            await _monitor.StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort: a failure to begin monitoring must never crash the
            // tray app; the status line simply stays at its last value.
        }
    }

    private void OnStatusChanged(object? sender, ConnectionStatus status)
        => _dispatcher.InvokeAsync(() => Apply(status));

    private void Apply(ConnectionStatus status)
    {
        _tray.SetStatus(ConnectionStatusText.ForStatus(status));
        if (status == ConnectionStatus.NoDevice)
        {
            ShowFirstRunGuidanceOnce();
        }
    }

    private void ShowFirstRunGuidanceOnce()
    {
        if (_firstRun.HasBeenShown)
        {
            return;
        }

        _firstRun.MarkShown();
        _tray.ShowNotification(GuidanceTitle, GuidanceMessage);
    }
}
