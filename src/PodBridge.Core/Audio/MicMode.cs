namespace PodBridge.Core.Audio;

/// <summary>
/// The active microphone / audio-link mode. Surfaces the unavoidable A2DP↔HFP
/// trade-off truthfully — <b>display only</b> in Phase 3; switching is Phase 4.
/// <see cref="Unknown"/> is an honest state for when the link mode cannot be read.
/// </summary>
public enum MicMode
{
    /// <summary>High-quality stereo media over A2DP; the mic is not engaged.</summary>
    HighQualityA2dp,

    /// <summary>HFP/call mode — mono, the mic is engaged; media quality collapses.</summary>
    CallModeHfp,

    /// <summary>Could not be determined; shown honestly, never guessed.</summary>
    Unknown,
}
