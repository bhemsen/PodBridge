using System.Windows.Threading;
using PodBridge.Core.Audio;

namespace PodBridge.App;

/// <summary>
/// Wires <see cref="AudioCollapseDetector"/> to the <see cref="TrayIcon"/> (issue #173):
/// shows the once-per-episode "Windows lost your audio devices" notification when the
/// detector's edge-triggered <see cref="AudioCollapseDetector.CollapseDetected"/> fires.
/// Clicking that notification — or the tray's "Audio recovery guide…" entry, wired
/// separately via <see cref="TrayIcon.SetAudioRecoveryHandler"/> in the composition root —
/// opens the <see cref="AudioRecoveryWindow"/>. UI updates are marshalled to the WPF
/// dispatcher (the detector's event can fire on a background/timer thread). Owns only the
/// event subscription; the detector's lifetime belongs to the DI container.
/// </summary>
public sealed class TrayAudioCollapseController : IDisposable
{
    private readonly TrayIcon _tray;
    private readonly AudioCollapseDetector _detector;
    private readonly Dispatcher _dispatcher;

    private TrayAudioCollapseController(TrayIcon tray, AudioCollapseDetector detector, Dispatcher dispatcher)
    {
        _tray = tray;
        _detector = detector;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates a controller binding <paramref name="detector"/> to <paramref name="tray"/>.
    /// Call <see cref="Start"/> to begin reacting to detections.
    /// </summary>
    public static TrayAudioCollapseController Create(
        TrayIcon tray, AudioCollapseDetector detector, Dispatcher dispatcher)
        => new(tray, detector, dispatcher);

    /// <summary>Subscribes to the detector. Must be called on the UI thread.</summary>
    public void Start() => _detector.CollapseDetected += OnCollapseDetected;

    /// <summary>Unsubscribes so no late detection touches a disposed tray.</summary>
    public void Dispose() => _detector.CollapseDetected -= OnCollapseDetected;

    private void OnCollapseDetected(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() => _tray.ShowAudioCollapseNotification(
            AudioCollapseGuidance.NotificationTitle, AudioCollapseGuidance.NotificationMessage));
}
