namespace PodBridge.Core.Capabilities;

/// <summary>
/// An optional Tier-2 feature that rides the driver-gated AAP L2CAP channel
/// (<see cref="Protocol.IAapTransport"/>). Availability is gated on BOTH driver presence
/// AND the (model, firmware-major) capability matrix (docs/research/firmware-capabilities.md).
/// </summary>
public enum Tier2Feature
{
    /// <summary>
    /// Noise-control switching (Off / ANC / Transparency / Adaptive as the model allows).
    /// Requires ANC hardware — a model-generation fact, not a firmware one (Apple support
    /// matrix, research source 7). Phase 6 ships this over the driver for the reference model.
    /// </summary>
    NoiseControl,

    /// <summary>
    /// Press-and-hold gesture remap (ClickHoldMode <c>0x16</c>). Phase 7 ships this for the
    /// AirPods Pro 2 reference model; the matrix delegates to the same
    /// <see cref="Protocol.GestureSupport"/> model gate so it never claims more than Phase 7.
    /// </summary>
    GestureRemap,

    /// <summary>
    /// Conversation Awareness — a model-gated feature (AirPods Pro 2 / Pro 3 per Apple's
    /// support matrix, research source 7); not a shipped feature yet, gated here for the
    /// Phase-8 capability surface.
    /// </summary>
    ConversationAwareness,
}
