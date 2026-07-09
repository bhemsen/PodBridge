using System.Windows.Threading;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.App;

/// <summary>
/// Wires the connection-gated <see cref="IDeviceStateProvider"/> to the
/// <see cref="TrayIcon"/>: renders the initial battery line and updates it live on
/// every state change (marshalled to the WPF UI thread, since the provider raises
/// its event from a WinRT/thread-pool thread). Rendering is delegated to the pure
/// <see cref="BatteryStatusText"/> mapper, so left/right/case %, charging, and the
/// "unknown / out of range" state are all decided in Core. Owns only the event
/// subscription; the provider's lifetime belongs to the DI container.
/// </summary>
public sealed class TrayBatteryController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly IDeviceStateProvider _provider;
    private readonly Dispatcher _dispatcher;

    private TrayBatteryController(
        TrayIcon tray,
        IDeviceStateProvider provider,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _provider = provider;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="provider"/> to
    /// <paramref name="tray"/>. Call <see cref="Start"/> to begin rendering.
    /// </summary>
    public static TrayBatteryController Create(
        TrayIcon tray,
        IDeviceStateProvider provider,
        Dispatcher dispatcher)
        => new(tray, provider, dispatcher);

    /// <summary>
    /// Subscribes to state changes and renders the current battery snapshot. Must be
    /// called on the UI thread (e.g. from app startup).
    /// </summary>
    public void Start()
    {
        _provider.StateChanged += OnStateChanged;
        Apply(_provider.Current);
    }

    /// <summary>Unsubscribes so no late event touches a disposed tray.</summary>
    public void Dispose() => _provider.StateChanged -= OnStateChanged;

    private void OnStateChanged(object? sender, DeviceState state)
        => _dispatcher.InvokeAsync(() => Apply(state));

    private void Apply(DeviceState state)
        => _tray.SetBattery(BatteryStatusText.ForState(state));
}
