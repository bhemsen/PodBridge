namespace PodBridge.Core.Protocol;

/// <summary>
/// The result of a <see cref="NoiseControlController.SetModeAsync"/> attempt.
/// Drives the optimistic-set UI: apply the mode immediately, then commit or revert
/// based on this outcome (docs/research/aap-anc-protocol.md "Echo / confirmation").
/// </summary>
public enum NoiseControlSetOutcome
{
    /// <summary>
    /// The Tier-2 transport was unavailable (driver absent): no frame was sent and the
    /// state is unchanged. The UI keeps switching disabled (graceful degradation).
    /// </summary>
    Unavailable,

    /// <summary>The AirPods echoed the requested mode: the change is confirmed.</summary>
    Confirmed,

    /// <summary>
    /// No matching echo arrived before the timeout (or the device echoed a different
    /// mode, e.g. Adaptive requested without the unlock frame): the optimistic change
    /// was reverted to the previous mode.
    /// </summary>
    RevertedOnTimeout,
}
