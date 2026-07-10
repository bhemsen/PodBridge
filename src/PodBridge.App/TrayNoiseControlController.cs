using System.Windows.Threading;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.App;

/// <summary>
/// Binds the Core Tier-2 <see cref="NoiseControlController"/> and the connection-gated
/// <see cref="IDeviceStateProvider"/> to the <see cref="TrayIcon"/> "Noise control"
/// submenu. The Core controller owns the optimistic-set / echo-confirm / timeout-revert
/// logic; this controller only renders it and forwards user picks:
/// <list type="bullet">
/// <item>A mode pick calls <see cref="NoiseControlController.SetModeAsync"/>; the
/// optimistic change is reflected immediately via <see cref="NoiseControlController.ModeChanged"/>,
/// and a <see cref="NoiseControlSetOutcome.RevertedOnTimeout"/> outcome raises a transient
/// error toast (the menu is rolled back by the same event).</item>
/// <item>Adaptive is gated on the connected model via
/// <see cref="NoiseControlSupport.SupportsAdaptive"/> (Pro 2 reference model).</item>
/// <item>When the transport is unavailable (advanced-tier driver absent — the default),
/// the modes are disabled and an honest explanation plus an "enable advanced tier"
/// affordance are shown; nothing is sent and no elevation happens (graceful degradation).</item>
/// </list>
/// The Core controller may raise <see cref="NoiseControlController.ModeChanged"/> from the
/// transport's background receive thread (the timeout-revert path), so updates are
/// marshalled to the WPF dispatcher. Owns only its subscriptions + handler wiring; the
/// Core controller's lifetime belongs to the DI container. Must be started on the UI
/// thread. (docs/architecture.md key flow 4; spec docs/specs/spec-advanced-driver-anc.md.)
/// </summary>
public sealed class TrayNoiseControlController : IDisposable
{
    /// <summary>
    /// Honest driver-absent copy. Makes no claim of a Microsoft-signed / production
    /// driver: the advanced tier is an opt-in, separately-installed, test-signed driver
    /// (spec docs/specs/spec-advanced-driver-anc.md; constitution honest-surface rule).
    /// </summary>
    internal const string UnavailableText =
        "Requires the optional advanced tier (driver not installed)";

    private const string ErrorTitle = "Noise control";
    private const string RevertedMessage =
        "Couldn't confirm the change with your AirPods — reverted.";

    private readonly TrayIcon _tray;
    private readonly NoiseControlController _controller;
    private readonly IDeviceStateProvider _stateProvider;
    private readonly Dispatcher _dispatcher;

    // Guards the once-per-connection AAP startup sequence: set when the session is kicked
    // off for a live device, re-armed when the device drops, so the handshake fires once
    // per connection rather than on every telemetry tick.
    private bool _sessionStarted;

    private TrayNoiseControlController(
        TrayIcon tray,
        NoiseControlController controller,
        IDeviceStateProvider stateProvider,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _controller = controller;
        _stateProvider = stateProvider;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="controller"/> and
    /// <paramref name="stateProvider"/> to <paramref name="tray"/>. Call
    /// <see cref="Start"/> to wire the submenu and render the initial state.
    /// </summary>
    public static TrayNoiseControlController Create(
        TrayIcon tray,
        NoiseControlController controller,
        IDeviceStateProvider stateProvider,
        Dispatcher dispatcher)
        => new(tray, controller, stateProvider, dispatcher);

    /// <summary>
    /// Wires the submenu action, subscribes to mode + device-state changes, renders the
    /// current availability/mode, and — if the driver is present and a device is live —
    /// starts the AAP session. Must be called on the UI thread.
    /// </summary>
    public void Start()
    {
        _tray.SetNoiseControlModeHandler(OnModeSelected);
        _controller.ModeChanged += OnModeChanged;
        _stateProvider.StateChanged += OnStateChanged;

        Render(_stateProvider.Current);
        TryStartSession(_stateProvider.Current);
    }

    /// <summary>Unsubscribes so no late event touches a disposed tray.</summary>
    public void Dispose()
    {
        _controller.ModeChanged -= OnModeChanged;
        _stateProvider.StateChanged -= OnStateChanged;
    }

    private void OnModeSelected(NoiseControlMode mode) => _ = ApplyModeAsync(mode);

    // Fire-and-forget from the menu click: the Core controller applies optimistically
    // (raising ModeChanged) and confirms/reverts on the echo. Only the timeout-revert
    // needs a UI reaction here — an honest transient toast; the menu itself is already
    // rolled back by the reverting ModeChanged event.
    private async Task ApplyModeAsync(NoiseControlMode mode)
    {
        var outcome = await _controller.SetModeAsync(mode).ConfigureAwait(false);
        if (outcome == NoiseControlSetOutcome.RevertedOnTimeout)
        {
            await _dispatcher.InvokeAsync(() => _tray.ShowNotification(ErrorTitle, RevertedMessage));
        }
    }

    private void OnModeChanged(object? sender, NoiseControlMode? mode)
        => _dispatcher.InvokeAsync(() => _tray.SetSelectedNoiseControlMode(mode));

    private void OnStateChanged(object? sender, DeviceState state)
        => _dispatcher.InvokeAsync(() =>
        {
            Render(state);
            TryStartSession(state);
        });

    private void Render(DeviceState state)
    {
        _tray.SetNoiseControlAvailability(
            _controller.IsAvailable,
            NoiseControlSupport.SupportsAdaptive(state.Model),
            UnavailableText);
        _tray.SetSelectedNoiseControlMode(_controller.CurrentMode);
    }

    // Runs the once-per-connection AAP startup sequence (handshake → set-specific-features
    // → request-notifications) when the driver is present and a device is live, so the
    // device honours SET frames and delivers the echo the confirm logic waits on. A no-op
    // when the driver is absent (Tier-1 default).
    private void TryStartSession(DeviceState state)
    {
        if (!_controller.IsAvailable || !state.IsLive)
        {
            _sessionStarted = false; // re-arm for the next live connection
            return;
        }

        if (_sessionStarted)
        {
            return;
        }

        _sessionStarted = true;
        _ = _controller.InitializeSessionAsync();
    }
}
