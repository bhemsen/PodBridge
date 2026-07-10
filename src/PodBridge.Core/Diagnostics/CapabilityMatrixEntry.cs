namespace PodBridge.Core.Diagnostics;

/// <summary>
/// One row of the diagnostics snapshot's capability matrix: a feature name (e.g.
/// <c>"Tier1.CaseBattery"</c> / <c>"Tier2.NoiseControl"</c>) plus the honest
/// <see cref="ICapabilityProvider"/>-derived verdict for the connected model — never a raw
/// bool with no explanation (constitution: Tier-2 is opt-in and honest).
/// </summary>
public sealed record CapabilityMatrixEntry
{
    /// <summary>The feature's axis-qualified name, e.g. <c>"Tier1.CaseBattery"</c>.</summary>
    public required string Feature { get; init; }

    /// <summary>Whether the feature is currently offered.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>
    /// The honest reason for the current state (<see cref="Capabilities.CapabilityDecision"/>'s
    /// reason strings for Tier-2; <c>"supported"</c> / <c>"not supported on this model"</c> for
    /// Tier-1, mirroring the same vocabulary).
    /// </summary>
    public required string Reason { get; init; }
}
