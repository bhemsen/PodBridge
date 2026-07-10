namespace PodBridge.Core.Protocol;

/// <summary>
/// Whether the press-and-hold gesture-remap settings surface can be offered for the
/// currently connected device. Drives the settings UI's three honest states so it is never
/// silently broken (constitution: graceful degradation, honest UX;
/// spec docs/specs/spec-gesture-remap.md).
/// </summary>
public enum GestureAvailability
{
    /// <summary>
    /// The optional Tier-2 driver is absent (the Tier-1 default): gesture assignment is
    /// unavailable and no packet is ever attempted. The UI shows the honest driver-absent
    /// notice and the opt-in "Enable advanced tier" affordance instead.
    /// </summary>
    DriverUnavailable,

    /// <summary>
    /// The driver is present but the connected model (or no connected model) has no
    /// remappable press-and-hold gesture within the Phase-7 supported set. The UI explains
    /// which models are supported rather than offering an unverified assignment.
    /// </summary>
    ModelUnsupported,

    /// <summary>
    /// The driver is present and the connected model supports the press-and-hold remap: the
    /// per-bud action pickers are enabled and an assignment can be applied and persisted.
    /// </summary>
    Available,
}
