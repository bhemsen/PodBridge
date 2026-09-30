using System.Windows.Threading;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.App;

/// <summary>
/// Binds the Core <see cref="BluetoothAudioLinkController"/> (one-click connect /
/// disconnect of the already-paired AirPods) and the live <see cref="IConnectionMonitor"/>
/// status to the <see cref="TrayIcon"/> "Connect AirPods" / "Disconnect AirPods" items.
/// The Core controller owns the send / confirm / re-send / timeout policy; this controller
/// only renders it and forwards clicks:
/// <list type="bullet">
/// <item>Each item is enabled per <see cref="BluetoothAudioLinkText.CanConnect"/> /
/// <see cref="BluetoothAudioLinkText.CanDisconnect"/> for the current status, and both are
/// disabled — with "Connecting…" / "Disconnecting…" — while an operation runs.</item>
/// <item>The operation runs on the thread pool (short synchronous COM calls plus a
/// confirmation wait of up to 45 s), so the UI thread is never blocked.</item>
/// <item>The outcome is shown as a notification; on any failure (no paired AirPods, no
/// connect control, declined, timed out) Windows Bluetooth settings are opened as the
/// fallback.</item>
/// </list>
/// Owns only its subscription, handler wiring and the shutdown cancellation; the Core
/// controller's lifetime belongs to the DI container. Must be started on the UI thread.
/// </summary>
public sealed class TrayConnectController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly BluetoothAudioLinkController _link;
    private readonly IConnectionMonitor _monitor;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _shutdown = new();

    private ConnectionStatus _status;
    private BluetoothAudioRequest? _pending;
    private bool _disposed;

    private TrayConnectController(
        TrayIcon tray,
        BluetoothAudioLinkController link,
        IConnectionMonitor monitor,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _link = link;
        _monitor = monitor;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="link"/> and <paramref name="monitor"/>
    /// to <paramref name="tray"/>. Call <see cref="Start"/> to wire the menu items.
    /// </summary>
    public static TrayConnectController Create(
        TrayIcon tray,
        BluetoothAudioLinkController link,
        IConnectionMonitor monitor,
        Dispatcher dispatcher)
        => new(tray, link, monitor, dispatcher);

    /// <summary>
    /// Wires the menu actions, subscribes to status changes and renders the items for the
    /// current status. Must be called on the UI thread.
    /// </summary>
    public void Start()
    {
        _tray.SetConnectionHandlers(OnConnect, OnDisconnect);
        _monitor.StatusChanged += OnStatusChanged;
        _status = _monitor.CurrentStatus;
        Render();
    }

    /// <summary>
    /// Unsubscribes and cancels an in-flight operation so no late result touches a
    /// disposed tray.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.StatusChanged -= OnStatusChanged;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void OnConnect() => _ = RunAsync(BluetoothAudioRequest.Connect);

    private void OnDisconnect() => _ = RunAsync(BluetoothAudioRequest.Disconnect);

    private void OnStatusChanged(object? sender, ConnectionStatus status)
        => _dispatcher.InvokeAsync(() =>
        {
            _status = status;
            if (!_disposed)
            {
                Render();
            }
        });

    // Starts on the UI thread (a menu click) and resumes there after the thread-pool
    // work, so the tray is only touched from the UI thread.
    private async Task RunAsync(BluetoothAudioRequest request)
    {
        if (_pending is not null || _disposed)
        {
            return;
        }

        _pending = request;
        Render();
        var outcome = await ExecuteAsync(request).ConfigureAwait(true);
        _pending = null;
        if (_disposed || outcome is null)
        {
            return;
        }

        Render();
        ShowOutcome(request, outcome.Value);
    }

    private async Task<BluetoothAudioLinkOutcome?> ExecuteAsync(BluetoothAudioRequest request)
    {
        var token = _shutdown.Token;
        try
        {
            return await Task.Run(
                () => request == BluetoothAudioRequest.Connect
                    ? _link.ConnectAsync(token)
                    : _link.DisconnectAsync(token),
                token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return null; // the app is shutting down
        }
        catch (Exception)
        {
            // Best-effort: a failure must never crash the tray; report it as declined,
            // which falls back to Windows Bluetooth settings.
            return BluetoothAudioLinkOutcome.Rejected;
        }
    }

    private void ShowOutcome(BluetoothAudioRequest request, BluetoothAudioLinkOutcome outcome)
    {
        var notice = BluetoothAudioLinkText.ForOutcome(request, outcome);
        if (notice is null)
        {
            return;
        }

        _tray.ShowNotification(notice.Title, notice.Message);
        if (notice.OpenBluetoothSettings)
        {
            TrayIcon.OpenBluetoothSettings();
        }
    }

    private void Render()
    {
        var idle = _pending is null;
        _tray.SetConnectItem(
            _pending == BluetoothAudioRequest.Connect
                ? BluetoothAudioLinkText.ConnectingLabel
                : BluetoothAudioLinkText.ConnectLabel,
            idle && BluetoothAudioLinkText.CanConnect(_status));
        _tray.SetDisconnectItem(
            _pending == BluetoothAudioRequest.Disconnect
                ? BluetoothAudioLinkText.DisconnectingLabel
                : BluetoothAudioLinkText.DisconnectLabel,
            idle && BluetoothAudioLinkText.CanDisconnect(_status));
    }
}
