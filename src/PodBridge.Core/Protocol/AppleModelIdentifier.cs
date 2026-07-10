using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Clean-room per-model shape mapper for the Apple-Continuity model identifiers
/// behind the vision's six supported AirPods models (docs/vision.md scope: AirPods
/// 2, 3, Pro, Pro 2, Pro 3, Max). Each case below cites the documented big-endian
/// hex identifier from docs/research/model-ids.md (issue #50's research comment —
/// the sole content authority for these values); no source or verbatim
/// documentation prose is copied (constitution: clean-room protocol).
/// <para>
/// Connector/case variants that already resolve to one Phase-2 <see cref="AirPodsModel"/>
/// enum member (Pro 2 Lightning/USB-C, Max Lightning/USB-C) share one shape entry
/// here, per the research's "fold connector variants into one AirPodsModel" decision.
/// Any other identifier — an unrecognised id, or a Phase-2 enum value outside the
/// vision's six models (e.g. 1st-gen AirPods, AirPods 4) — degrades to
/// <see cref="UnknownFallback"/>.
/// </para>
/// </summary>
public static class AppleModelIdentifier
{
    /// <summary>
    /// The labelled generic fallback: best-effort dual-bud battery/in-ear parsing
    /// (never throws, never shows nothing), with model-specific (Tier-2) features left
    /// for the capability provider to disable (docs/research/model-ids.md
    /// "Unknown-identifier fallback").
    /// </summary>
    public static readonly AirPodsModelInfo UnknownFallback = new()
    {
        Model = AirPodsModel.Unknown,
        DisplayName = "Unknown AirPods",
        HasDualBuds = true,
        HasBatteryCase = true,
        HasInEarDetection = true,
        IsRecognized = false,
    };

    /// <summary>
    /// Resolves <paramref name="model"/> to its per-model shape, or
    /// <see cref="UnknownFallback"/> when it is not one of the vision's six models.
    /// </summary>
    public static AirPodsModelInfo Resolve(AirPodsModel model) => model switch
    {
        // AirPods 2 = 0x0F20 (documented Apple-Continuity model id; see docs/research/model-ids.md).
        AirPodsModel.AirPods2 => DualBud(model, "AirPods 2"),

        // AirPods 3 = 0x1320 (documented Apple-Continuity model id; see docs/research/model-ids.md).
        AirPodsModel.AirPods3 => DualBud(model, "AirPods 3"),

        // AirPods Pro = 0x0E20 (documented Apple-Continuity model id; see docs/research/model-ids.md).
        AirPodsModel.AirPodsPro => DualBud(model, "AirPods Pro"),

        // AirPods Pro 2, Lightning case = 0x1420 (documented Apple-Continuity model id;
        // see docs/research/model-ids.md).
        AirPodsModel.AirPodsPro2 => DualBud(model, "AirPods Pro 2"),

        // AirPods Pro 2, USB-C case = 0x2420 (documented Apple-Continuity model id; see
        // docs/research/model-ids.md) — same shape as Lightning, connector-only difference.
        AirPodsModel.AirPodsPro2UsbC => DualBud(model, "AirPods Pro 2"),

        // AirPods Pro 3 = 0x2720 (documented Apple-Continuity model id; see
        // docs/research/model-ids.md; flagged there for a real-hardware re-check at
        // the Phase 8 human QA gate per the research's disputes section).
        AirPodsModel.AirPodsPro3 => DualBud(model, "AirPods Pro 3"),

        // AirPods Max, Lightning = 0x0A20 (documented Apple-Continuity model id; see
        // docs/research/model-ids.md) — single over-ear unit: no battery-reporting
        // case (the "Smart Case" is a sleep cover, not a lid-sensing charging case),
        // head on/off detection instead of in-ear.
        AirPodsModel.AirPodsMax => HeadWorn(model, "AirPods Max"),

        // AirPods Max, USB-C = 0x1F20 (documented Apple-Continuity model id; see
        // docs/research/model-ids.md) — same shape as Lightning, connector-only difference.
        AirPodsModel.AirPodsMaxUsbC => HeadWorn(model, "AirPods Max"),

        _ => UnknownFallback,
    };

    private static AirPodsModelInfo DualBud(AirPodsModel model, string displayName) => new()
    {
        Model = model,
        DisplayName = displayName,
        HasDualBuds = true,
        HasBatteryCase = true,
        HasInEarDetection = true,
    };

    private static AirPodsModelInfo HeadWorn(AirPodsModel model, string displayName) => new()
    {
        Model = model,
        DisplayName = displayName,
        HasDualBuds = false,
        HasBatteryCase = false,
        HasInEarDetection = false,
    };
}
