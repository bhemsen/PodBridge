using System.Windows;
using System.Windows.Controls;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.App;

/// <summary>
/// The Tier-2 (opt-in) gesture-remap settings surface: lets the user reassign the AirPods
/// press-and-hold action per bud. A thin binding over the Core
/// <see cref="GestureSettingsController"/> (which owns the availability decision, the
/// supported-action set, persistence, and the write over the transport) plus the shared
/// <see cref="IDeviceStateProvider"/> for the connected model — mirroring how
/// <see cref="TrayNoiseControlController"/> binds the Core noise-control logic.
/// <list type="bullet">
/// <item>Driver present + supported model → per-bud pickers (honest action set) + Apply;
/// the choice is persisted and re-applied on every reconnect via the Core re-push policy.</item>
/// <item>Driver absent (Tier-1 default) → the pickers are replaced by the <b>reused</b>
/// Phase-6 driver-absent notice (<see cref="TrayNoiseControlController.UnavailableText"/>,
/// no new signed-driver claim) and the opt-in "Enable advanced tier" affordance — never
/// silently broken (constitution: graceful degradation, honest UX).</item>
/// <item>Driver present but the model is out of scope → an honest "connect supported
/// AirPods" explanation; no packet is attempted.</item>
/// </list>
/// </summary>
public partial class GestureSettingsWindow : Window
{
    private readonly GestureSettingsController _controller;
    private readonly IDeviceStateProvider _stateProvider;
    private readonly Action _onEnableAdvancedTier;

    // The model the panels were last rendered for; guards against re-rendering (and
    // clobbering the pickers) on routine telemetry ticks — availability only changes when
    // the connected model changes (the transport's availability is fixed at runtime).
    private AirPodsModel? _renderedModel;

    public GestureSettingsWindow(
        GestureSettingsController controller,
        IDeviceStateProvider stateProvider,
        Action onEnableAdvancedTier)
    {
        InitializeComponent();
        _controller = controller;
        _stateProvider = stateProvider;
        _onEnableAdvancedTier = onEnableAdvancedTier;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // A combo entry: the wire action plus its honest, human-readable label.
    private sealed record ActionChoice(GestureAction Action, string Label);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Render(_stateProvider.Current);
        _stateProvider.StateChanged += OnStateChanged;
    }

    private void OnClosed(object? sender, EventArgs e)
        => _stateProvider.StateChanged -= OnStateChanged;

    // Re-render only when the connected model actually changed (e.g. a supported device
    // connects while the window is open), marshalled to the UI thread — routine battery /
    // in-ear ticks carry the same model and must not reset the pickers mid-edit.
    private void OnStateChanged(object? sender, DeviceState state)
        => Dispatcher.InvokeAsync(() =>
        {
            if (state.Model != _renderedModel)
            {
                Render(state);
            }
        });

    private void Render(DeviceState state)
    {
        _renderedModel = state.Model;
        var availability = _controller.GetAvailability(state.Model);

        AvailablePanel.Visibility = Collapse(availability == GestureAvailability.Available);
        DriverUnavailablePanel.Visibility =
            Collapse(availability == GestureAvailability.DriverUnavailable);
        ModelUnsupportedPanel.Visibility =
            Collapse(availability == GestureAvailability.ModelUnsupported);

        if (availability == GestureAvailability.Available)
        {
            PopulatePickers(state.Model);
        }
        else if (availability == GestureAvailability.DriverUnavailable)
        {
            // Reuse the Phase-6 notice verbatim; adds no new signed-driver claim.
            UnavailableText.Text = TrayNoiseControlController.UnavailableText;
        }

        StatusText.Visibility = Visibility.Collapsed;
    }

    // Fills both pickers with the model's honest action set and pre-selects the persisted
    // assignment (or the first action when the user has never chosen one — a UI default, not
    // a read of the device's current state).
    private void PopulatePickers(AirPodsModel model)
    {
        var choices = GestureSupport.AvailableActions(model).Select(ToChoice).ToList();
        var current = _controller.Current;
        BindPicker(LeftActionCombo, choices, current?.LeftBud);
        BindPicker(RightActionCombo, choices, current?.RightBud);
    }

    private static void BindPicker(
        ComboBox combo, IReadOnlyList<ActionChoice> choices, GestureAction? selected)
    {
        combo.ItemsSource = choices;
        combo.SelectedValue = selected ?? choices[0].Action;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (LeftActionCombo.SelectedValue is not GestureAction left
            || RightActionCombo.SelectedValue is not GestureAction right)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        try
        {
            var outcome = await _controller.ApplyAsync(new GestureConfiguration(right, left));
            ShowStatus(OutcomeMessage(outcome));
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }

    private void OnEnableAdvancedTier(object sender, RoutedEventArgs e) => _onEnableAdvancedTier();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    private static string OutcomeMessage(GestureRepushOutcome outcome) => outcome switch
    {
        GestureRepushOutcome.Confirmed => "Applied to your AirPods.",
        GestureRepushOutcome.CouldNotApply =>
            "Saved. Your AirPods didn't confirm it just now, so it'll be re-applied the next "
            + "time they reconnect.",
        _ => "The advanced-tier driver isn't available, so nothing was changed.",
    };

    private static ActionChoice ToChoice(GestureAction action) => action switch
    {
        GestureAction.NoiseControl => new ActionChoice(action, "Cycle noise control"),
        GestureAction.Siri => new ActionChoice(action, "Siri / voice assistant"),
        _ => new ActionChoice(action, action.ToString()),
    };

    private static Visibility Collapse(bool visible)
        => visible ? Visibility.Visible : Visibility.Collapsed;
}
