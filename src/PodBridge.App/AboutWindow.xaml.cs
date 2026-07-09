using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using PodBridge.Core.Startup;

namespace PodBridge.App;

/// <summary>
/// The application's first non-tray window: a read-only About surface reachable
/// from the tray "About" entry. Shows the coined product name, the "for AirPods"
/// descriptor, the mandatory not-affiliated disclaimer, the honest audio/mic note,
/// the Apache-2.0 license line, third-party notices, the app version, and links to
/// the user docs and project page. Static content comes from its
/// <see cref="AboutViewModel"/>; the opt-in auto-start-at-login checkbox reflects
/// and sets the MSIX <c>StartupTask</c> state via <see cref="IStartupToggle"/>
/// (default OFF).
/// </summary>
public partial class AboutWindow : Window
{
    private readonly IStartupToggle _startupToggle;

    // Guards the Checked/Unchecked handlers while we set IsChecked programmatically
    // to reflect the current state, so reflecting a state never re-issues a request.
    private bool _suppressToggle;

    public AboutWindow(AboutViewModel viewModel, IStartupToggle startupToggle)
    {
        InitializeComponent();
        DataContext = viewModel;
        _startupToggle = startupToggle;
        Loaded += OnLoaded;
    }

    // Reflect the current auto-start state once the window is up. The checkbox
    // starts unchecked (default OFF) and is corrected here to the real state.
    private async void OnLoaded(object sender, RoutedEventArgs e)
        => ApplyState(await _startupToggle.GetStateAsync());

    private async void OnAutoStartChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle)
        {
            return;
        }

        ApplyState(await _startupToggle.RequestEnableAsync());
    }

    private async void OnAutoStartUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle)
        {
            return;
        }

        ApplyState(await _startupToggle.DisableAsync());
    }

    // Mirror the toggle to the resolved state: checked only when Enabled; disabled
    // with an explanatory hint when Windows (the user or policy) blocks it — the
    // app cannot override that, so it is surfaced honestly rather than silently
    // re-checked.
    private void ApplyState(StartupToggleState state)
    {
        var blocked = state is StartupToggleState.DisabledByUser
            or StartupToggleState.DisabledByPolicy;

        _suppressToggle = true;
        AutoStartCheckBox.IsChecked = state == StartupToggleState.Enabled;
        AutoStartCheckBox.IsEnabled = !blocked;
        AutoStartHint.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        _suppressToggle = false;
    }

    // Open docs/project links in the user's default browser (local-only: no
    // in-app web view). A failure to launch must never crash the tray app.
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Best-effort: nothing to do if the shell cannot open the link.
        }

        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
