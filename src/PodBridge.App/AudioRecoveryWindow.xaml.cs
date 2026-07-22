using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using PodBridge.Core.Audio;

namespace PodBridge.App;

/// <summary>
/// The audio-stack-collapse recovery guide (issue #173): opened from the tray
/// notification, or the "Audio recovery guide…" menu entry, when
/// <see cref="AudioCollapseDetector"/> detects that Windows dropped its whole audio
/// device set. Renders the honest, static copy from <see cref="AudioCollapseGuidance"/>
/// verbatim — a Windows-level failure, not a PodBridge bug, that PodBridge cannot fix
/// directly. The "Open Services" button shell-launches <c>services.msc</c>
/// (<c>UseShellExecute = true</c>; PodBridge requests no elevation of its own —
/// Windows itself prompts for admin to restart a service there, keeping the
/// <c>asInvoker</c> Tier-1 guarantee); PodBridge never restarts a service itself. A
/// single instance is reused, mirroring the About/Gesture windows; closing it never
/// exits the tray-resident app (ShutdownMode is OnExplicitShutdown).
/// </summary>
public partial class AudioRecoveryWindow : Window
{
    public AudioRecoveryWindow()
    {
        InitializeComponent();
        HeaderText.Text = AudioCollapseGuidance.Title;
        ExplanationText.Text = AudioCollapseGuidance.Explanation;
        Step1TitleText.Text = AudioCollapseGuidance.Step1Title;
        Step1BodyText.Text = AudioCollapseGuidance.Step1Body;
        Step2TitleText.Text = AudioCollapseGuidance.Step2Title;
        Step2BodyText.Text = AudioCollapseGuidance.Step2Body;
        OpenServicesButton.Content = AudioCollapseGuidance.OpenServicesButtonLabel;
        Step3TitleText.Text = AudioCollapseGuidance.Step3Title;
        Step3BodyText.Text = AudioCollapseGuidance.Step3Body;
    }

    // Shell-launches services.msc with no elevation requested by PodBridge — Windows
    // itself prompts the user for admin to restart a service there; PodBridge never
    // restarts a service itself (constitution: Tier 1 needs no elevation).
    private void OnOpenServices(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                "Could not open Windows Services.",
                "PodBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
