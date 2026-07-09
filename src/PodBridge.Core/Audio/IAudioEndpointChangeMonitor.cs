namespace PodBridge.Core.Audio;

/// <summary>
/// Observes audio device-topology changes (an endpoint added, removed, or the default
/// device changed) so the mic-profile policy can re-evaluate — in particular so the
/// single-device degrade warning updates live when the fallback mic appears or
/// disappears. The OS detection lives entirely in the Windows adapter; Core only
/// reacts by calling <see cref="MicPolicyEngine.Refresh"/>. Implemented on Windows via
/// the Core Audio <c>IMMNotificationClient</c> callback.
/// </summary>
public interface IAudioEndpointChangeMonitor
{
    /// <summary>
    /// Raised after the audio endpoint topology changes. May fire on a background
    /// thread; handlers must be thread-safe (the engine's <c>Refresh</c> is).
    /// </summary>
    event EventHandler? EndpointsChanged;

    void Start();

    void Stop();
}
