namespace PodBridge.Core.Audio;

/// <summary>
/// Pure, device-independent mapping from a read <see cref="AudioState"/> to honest
/// <see cref="AudioGuidance"/> (display + advice). Lives in Core so the constitution's
/// Tier-1 test gate can exercise it with a fake <see cref="IAudioStateReader"/>; the App
/// only renders the result. Guidance is generic (Windows 11 21H2+, update the Bluetooth
/// adapter driver, prefer an AAC-capable adapter/dongle) and — per constitution Don'ts +
/// "Honest audio surface" — never claims Apple-parity sound, never recommends the paid
/// "Alternative A2DP Driver"/FDK-AAC, and never offers a "force AAC" action. Advice fires
/// only on positively-confirmed SBC; AAC and Unknown produce no advice.
/// </summary>
public static class AudioGuidanceEngine
{
    /// <summary>Codec tray line for AAC — the best available quality on Windows.</summary>
    public const string CodecLineAac = "Codec: AAC (best available on Windows)";

    /// <summary>Codec tray line for the SBC fallback.</summary>
    public const string CodecLineSbc = "Codec: SBC";

    /// <summary>Codec tray line when the negotiated codec cannot be determined.</summary>
    public const string CodecLineUnknown = "Codec: couldn't determine";

    /// <summary>Mic tray line for high-quality stereo media over A2DP.</summary>
    public const string MicLineHighQuality = "Mic: High quality (A2DP)";

    /// <summary>Mic tray line for HFP/call mode.</summary>
    public const string MicLineCallMode = "Mic: Call mode (mono)";

    /// <summary>Mic tray line when the audio-link mode cannot be determined.</summary>
    public const string MicLineUnknown = "Mic: couldn't determine";

    /// <summary>Generic, driver-free AAC guidance shown only on confirmed SBC.</summary>
    public const string AacAdvice =
        "Audio is using the lower-quality SBC codec. To reach AAC, make sure you are on " +
        "Windows 11 21H2 or later, update your Bluetooth adapter driver, and prefer an " +
        "AAC-capable Bluetooth adapter or dongle.";

    /// <summary>Maps <paramref name="state"/> to honest display + advice (never null).</summary>
    public static AudioGuidance ForState(AudioState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var showAdvice = state.Codec == CodecKind.Sbc;
        return new AudioGuidance(
            CodecLineFor(state.Codec),
            MicModeLineFor(state.Mic),
            showAdvice ? AacAdvice : null,
            showAdvice);
    }

    private static string CodecLineFor(CodecKind codec) => codec switch
    {
        CodecKind.Aac => CodecLineAac,
        CodecKind.Sbc => CodecLineSbc,
        _ => CodecLineUnknown,
    };

    private static string MicModeLineFor(MicMode mic) => mic switch
    {
        MicMode.HighQualityA2dp => MicLineHighQuality,
        MicMode.CallModeHfp => MicLineCallMode,
        _ => MicLineUnknown,
    };
}
