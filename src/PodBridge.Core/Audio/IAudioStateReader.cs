namespace PodBridge.Core.Audio;

/// <summary>
/// Reads the current <see cref="AudioState"/> (negotiated codec + active mic/audio-link
/// mode) for the connected AirPods. <b>Read-only</b>: it never sets or switches an
/// endpoint — that is Phase 4's <see cref="IAudioPolicy"/>. Implemented on Windows by
/// <c>WindowsAudioStateReader</c> using driver-free, admin-free user-mode mechanisms;
/// returns the <see cref="CodecKind.Unknown"/> / <see cref="MicMode.Unknown"/> state
/// honestly when it cannot determine a value (constitution "Honest audio surface").
/// </summary>
public interface IAudioStateReader
{
    /// <summary>
    /// Reads the current audio state on demand (on device-connect and on a manual
    /// "Refresh audio status" action — not by continuous polling). Never returns null
    /// and never throws for an undeterminable value; it reports <c>Unknown</c> instead.
    /// </summary>
    AudioState Read();
}
