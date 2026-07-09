using PodBridge.Core.Audio;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent <see cref="IAudioSessionMonitor"/> that lets a test raise
/// comms-capture-session open/close on demand, driving the engine's Auto-switch
/// promote/restore with no physical mic session (constitution Tier-1 test gate).
/// </summary>
internal sealed class FakeAudioSessionMonitor : IAudioSessionMonitor
{
    public event EventHandler? CommunicationsCaptureStarted;

    public event EventHandler? CommunicationsCaptureStopped;

    public bool IsStarted { get; private set; }

    public void Start() => IsStarted = true;

    public void Stop() => IsStarted = false;

    /// <summary>Simulates a communications capture (mic) session opening.</summary>
    public void RaiseCaptureStarted()
        => CommunicationsCaptureStarted?.Invoke(this, EventArgs.Empty);

    /// <summary>Simulates the communications capture (mic) session closing.</summary>
    public void RaiseCaptureStopped()
        => CommunicationsCaptureStopped?.Invoke(this, EventArgs.Empty);
}
