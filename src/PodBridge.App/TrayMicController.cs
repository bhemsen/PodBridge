using System.Windows.Threading;
using PodBridge.Core.Audio;

namespace PodBridge.App;

/// <summary>
/// Binds the <see cref="MicPolicyEngine"/> to the <see cref="TrayIcon"/> "Microphone
/// mode" submenu: restores the persisted <see cref="MicPolicyMode"/> at startup
/// (default HiFi-lock), drives the engine live when the user picks a mode or flips the
/// Call-mode toggle, persists the chosen mode via <see cref="MicPolicyModeStore"/>, and
/// surfaces the honest single-device degrade warning. The engine may raise its
/// <see cref="MicPolicyEngine.NoAlternateMicWarningChanged"/> event from the session
/// monitor's background COM thread, so warning updates are marshalled to the WPF
/// dispatcher. Owns only its subscription + handler wiring; the engine's lifetime
/// belongs to the DI container. Must be started on the UI thread.
/// </summary>
public sealed class TrayMicController : IDisposable
{
    private const string WarningTitle = "Microphone";

    private readonly TrayIcon _tray;
    private readonly MicPolicyEngine _engine;
    private readonly MicPolicyModeStore _store;
    private readonly Dispatcher _dispatcher;

    // Guards the one-shot degrade toast: set when shown, re-armed when the warning
    // clears, so the toast fires once per transition into the degraded state (never
    // repeatedly while it stays degraded).
    private bool _warningToastShown;

    // Same one-shot guard for the distinct "AirPods mic unavailable" warning.
    private bool _unavailableToastShown;

    private TrayMicController(
        TrayIcon tray,
        MicPolicyEngine engine,
        MicPolicyModeStore store,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _engine = engine;
        _store = store;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="engine"/> to <paramref name="tray"/>,
    /// persisting through <paramref name="store"/>. Call <see cref="Start"/> to restore
    /// the persisted mode and render the submenu.
    /// </summary>
    public static TrayMicController Create(
        TrayIcon tray,
        MicPolicyEngine engine,
        MicPolicyModeStore store,
        Dispatcher dispatcher)
        => new(tray, engine, store, dispatcher);

    /// <summary>
    /// Wires the submenu actions, subscribes to the degrade warning, restores the
    /// persisted mode into the engine, and renders the current menu state. Must be
    /// called on the UI thread.
    /// </summary>
    public void Start()
    {
        _tray.SetMicPolicyHandlers(OnModeSelected, OnCallModeToggled);
        _engine.NoAlternateMicWarningChanged += OnWarningChanged;
        _engine.AirPodsMicUnavailableChanged += OnUnavailableChanged;

        // Restore the persisted mode (default HiFi-lock on first run) into the engine,
        // which re-applies the endpoint-role assignment, then reflect it in the menu.
        _engine.SetMode(_store.Load());

        // The engine raises its warning events from its constructor — before this controller
        // could subscribe — so the initial state is never delivered as an event. Replay both
        // current warning states here so the menu lines AND the one-shot toasts fire at launch.
        ApplyWarning(_engine.NoAlternateMicWarning);
        ApplyUnavailable(_engine.AirPodsMicUnavailable);
    }

    /// <summary>Unsubscribes so no late warning event touches a disposed tray.</summary>
    public void Dispose()
    {
        _engine.NoAlternateMicWarningChanged -= OnWarningChanged;
        _engine.AirPodsMicUnavailableChanged -= OnUnavailableChanged;
    }

    private void OnModeSelected(MicPolicyMode mode)
    {
        _engine.SetMode(mode);
        _store.Save(mode);
        Render();
    }

    private void OnCallModeToggled()
    {
        // The engine is the source of truth: flip its current toggle state and re-sync
        // the menu from it (ignoring WPF's own auto-toggle of the checkable item).
        _engine.SetCallModeActive(!_engine.CallModeActive);
        Render();
    }

    private void OnWarningChanged(object? sender, bool degraded)
        => _dispatcher.InvokeAsync(() => ApplyWarning(degraded));

    private void OnUnavailableChanged(object? sender, bool unavailable)
        => _dispatcher.InvokeAsync(() => ApplyUnavailable(unavailable));

    private void ApplyWarning(bool degraded)
    {
        Render();

        if (!degraded)
        {
            _warningToastShown = false; // re-arm for the next transition into degraded
            return;
        }

        // On the transition into the degraded state, also raise a one-shot toast so the
        // honesty warning is noticed even if the menu is closed — never a silent HFP.
        // The guard keeps it one-shot: it fires once per degrade, not on every replay.
        if (!_warningToastShown)
        {
            _warningToastShown = true;
            _tray.ShowNotification(WarningTitle, MicPolicyEngine.NoAlternateMicWarningText);
        }
    }

    private void ApplyUnavailable(bool unavailable)
    {
        Render();

        if (!unavailable)
        {
            _unavailableToastShown = false; // re-arm for the next transition into unavailable
            return;
        }

        // One-shot toast on the transition into the unavailable state so the honest warning
        // is noticed even with the menu closed — never a silent no-op.
        if (!_unavailableToastShown)
        {
            _unavailableToastShown = true;
            _tray.ShowNotification(WarningTitle, MicPolicyEngine.AirPodsMicUnavailableText);
        }
    }

    private void Render()
    {
        _tray.SetSelectedMicMode(_engine.CurrentMode);
        _tray.SetCallModeActive(_engine.CallModeActive);
        _tray.SetMicWarning(_engine.NoAlternateMicWarning, MicPolicyEngine.NoAlternateMicWarningText);
        _tray.SetAirPodsMicUnavailable(
            _engine.AirPodsMicUnavailable, MicPolicyEngine.AirPodsMicUnavailableText);
    }
}
