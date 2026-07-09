using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace PodBridge.App;

/// <summary>
/// The application's first non-tray window: a read-only About surface reachable
/// from the tray "About" entry. Shows the coined product name, the "for AirPods"
/// descriptor, the mandatory not-affiliated disclaimer, the honest audio/mic note,
/// the Apache-2.0 license line, third-party notices, the app version, and links to
/// the user docs and project page. All content comes from its <see cref="AboutViewModel"/>.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow(AboutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
