namespace PodBridge.Core.Models;

/// <summary>
/// Per-model shape produced by <see cref="Protocol.IModelRegistry"/>: dual-bud vs
/// single unit, whether the case reports its own battery, and in-ear vs head on/off
/// detection. Tier-1 features gate on this model axis only, never on firmware
/// (constitution: Tier-1 independence) — e.g. AirPods Max has no case, so case
/// battery is hidden regardless of driver presence.
/// </summary>
public sealed record AirPodsModelInfo
{
    /// <summary>The model this shape describes.</summary>
    public required AirPodsModel Model { get; init; }

    /// <summary>Human-readable name for the UI, e.g. "AirPods Pro" or "Unknown AirPods".</summary>
    public required string DisplayName { get; init; }

    /// <summary>True for the dual-earbud models; false for the single over-ear AirPods Max.</summary>
    public required bool HasDualBuds { get; init; }

    /// <summary>True when the case reports its own battery percentage.</summary>
    public required bool HasBatteryCase { get; init; }

    /// <summary>
    /// True when the model uses in-ear detection; false means head on/off detection
    /// (AirPods Max) instead.
    /// </summary>
    public required bool HasInEarDetection { get; init; }

    /// <summary>
    /// False only for the generic "Unknown AirPods" fallback — signals to callers
    /// (e.g. the Phase-8 capability provider) that model-specific features should stay
    /// off even though best-effort battery/in-ear fields are still populated.
    /// </summary>
    public bool IsRecognized { get; init; } = true;
}
