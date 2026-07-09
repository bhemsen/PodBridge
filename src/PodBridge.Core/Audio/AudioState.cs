namespace PodBridge.Core.Audio;

/// <summary>
/// An immutable snapshot of the read-only audio surface: the negotiated media
/// <see cref="Codec"/> and the active <see cref="Mic"/> (audio-link) mode. Produced
/// by <see cref="IAudioStateReader"/> and mapped to honest display + advice text by
/// <see cref="AudioGuidanceEngine"/>. Platform-neutral — no OS types leak.
/// </summary>
/// <param name="Codec">The actually-negotiated A2DP media codec.</param>
/// <param name="Mic">The active microphone / audio-link mode.</param>
public sealed record AudioState(CodecKind Codec, MicMode Mic)
{
    /// <summary>Neutral snapshot when nothing could be determined (e.g. no device).</summary>
    public static AudioState Unknown { get; } = new(CodecKind.Unknown, MicMode.Unknown);
}
