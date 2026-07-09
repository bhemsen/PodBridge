namespace PodBridge.Core.Audio;

/// <summary>
/// The honest, ready-to-render output of <see cref="AudioGuidanceEngine"/> for one
/// <see cref="AudioState"/>: a tray codec line, a tray mic-mode line, and — only when
/// SBC fallback is positively confirmed — the generic AAC <see cref="Advice"/> plus
/// <see cref="ShowAacAdvice"/> = <c>true</c> so the App raises the guidance
/// notification. On AAC (best available) and on <c>Unknown</c> (honestly undetermined)
/// no advice fires. No string here claims Apple-parity sound, recommends a paid audio
/// driver, or offers a "force AAC" action (constitution Don'ts + "Honest audio surface").
/// </summary>
/// <param name="CodecLine">Tray line for the negotiated codec (never null/empty).</param>
/// <param name="MicModeLine">Tray line for the active mic/audio-link mode (never null/empty).</param>
/// <param name="Advice">Generic AAC guidance when SBC is confirmed; otherwise <c>null</c>.</param>
/// <param name="ShowAacAdvice"><c>true</c> only on confirmed SBC, gating the notification.</param>
public sealed record AudioGuidance(
    string CodecLine,
    string MicModeLine,
    string? Advice,
    bool ShowAacAdvice);
