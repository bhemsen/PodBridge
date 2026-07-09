namespace PodBridge.Core.Audio;

/// <summary>
/// Observes when a communications capture (microphone) session opens or closes,
/// so the mic-profile policy can react (e.g. auto-switch mode). Implemented on
/// Windows via IAudioSessionManager2 notifications.
/// </summary>
public interface IAudioSessionMonitor
{
    event EventHandler? CommunicationsCaptureStarted;

    event EventHandler? CommunicationsCaptureStopped;

    void Start();

    void Stop();
}
