namespace PodBridge.Core.Audio;

/// <summary>
/// The active microphone-profile policy mode. Governs how the engine routes the
/// communications role between the AirPods and a non-AirPods fallback device.
/// The default is <see cref="HiFiLock"/> (spec prior decision: "great by default").
/// </summary>
public enum MicPolicyMode
{
    /// <summary>
    /// AirPods stay the default (media) render device; the default-communications
    /// render <b>and</b> capture point at a non-AirPods fallback, so opening a comms
    /// mic session never forces HFP on the AirPods (media stays A2DP).
    /// </summary>
    HiFiLock,

    /// <summary>
    /// Promotes the AirPods to the communications role (render + capture) while a
    /// comms capture session is live, then restores the <see cref="HiFiLock"/>
    /// assignment when the session closes.
    /// </summary>
    AutoSwitch,

    /// <summary>
    /// A manual toggle swaps the communications role (render + capture) to/from the
    /// AirPods on demand, independent of any live session.
    /// </summary>
    CallMode,
}
