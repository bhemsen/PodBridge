using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Decoded result of one Apple-Continuity proximity-pairing (type <c>0x07</c>)
/// message, with primary/secondary already resolved to physical left/right per the
/// status-bit-5 flip (and the bit-5 ⊕ bit-6 flip for in-ear). Battery percentages
/// are <see langword="null"/> when the source nibble is the unknown sentinel.
/// See docs/research/continuity-parser.md for the byte-level derivation.
/// </summary>
public sealed record ContinuityProximityData
{
    /// <summary>Identified model from the model-id bytes, or <see cref="AirPodsModel.Unknown"/>.</summary>
    public required AirPodsModel Model { get; init; }

    /// <summary>Left-bud battery %, or <see langword="null"/> if unknown/absent.</summary>
    public int? LeftBatteryPercent { get; init; }

    /// <summary>Right-bud battery %, or <see langword="null"/> if unknown/absent.</summary>
    public int? RightBatteryPercent { get; init; }

    /// <summary>Case battery %, or <see langword="null"/> if unknown/absent.</summary>
    public int? CaseBatteryPercent { get; init; }

    /// <summary>True while the left bud is charging.</summary>
    public bool LeftCharging { get; init; }

    /// <summary>True while the right bud is charging.</summary>
    public bool RightCharging { get; init; }

    /// <summary>True while the case is charging.</summary>
    public bool CaseCharging { get; init; }

    /// <summary>True while the left bud is in an ear.</summary>
    public bool LeftInEar { get; init; }

    /// <summary>True while the right bud is in an ear.</summary>
    public bool RightInEar { get; init; }

    /// <summary>
    /// Lid open (<see langword="true"/>) / closed (<see langword="false"/>), or
    /// <see langword="null"/> when the lid bit is not trustworthy for this frame
    /// (only reliable for in-case broadcasts).
    /// </summary>
    public bool? LidOpen { get; init; }
}
