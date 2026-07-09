namespace PodBridge.Core.Audio;

/// <summary>
/// The actually-negotiated A2DP media codec for the connected AirPods.
/// <see cref="Unknown"/> is a first-class honest state for when Windows will not
/// reveal the negotiated codec — the tool says so rather than guessing
/// (constitution "Honest audio surface").
/// </summary>
public enum CodecKind
{
    /// <summary>AAC — the best available AirPods media quality on Windows 11.</summary>
    Aac,

    /// <summary>SBC — the lower-quality fallback; AAC guidance applies.</summary>
    Sbc,

    /// <summary>Could not be determined; shown honestly, never guessed.</summary>
    Unknown,
}
