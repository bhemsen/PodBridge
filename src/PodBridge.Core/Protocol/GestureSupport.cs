using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Which press-and-hold gesture capability a connected AirPods model exposes.
/// Device-independent business logic (kept in OS-free Core so the settings surface can
/// gate itself without a hardware dependency), mirroring <see cref="NoiseControlSupport"/>.
/// <para>
/// Phase 7 targets the same reference model the Phase-6 driver + AAP path already support —
/// AirPods Pro 2 (USB-C fw <c>7A305</c>) — because the gesture byte-format is confirmed only
/// there; unsupported models hide the feature and the broad (model, firmware) matrix is
/// Phase 8 (issue #53). This deliberately stays tighter than the research's wider capability
/// note (which additionally lists Pro 3 / ANC AirPods 4 per-bud and AirPods Max Siri-only) so
/// no unverified opcode is ever sent (docs/research/gesture-aap.md "confirmed model/firmware";
/// spec docs/specs/spec-gesture-remap.md decision "targets the models the Phase-6 driver + AAP
/// path already support (Pro 2 USB-C pinned)").
/// </para>
/// </summary>
public static class GestureSupport
{
    /// <summary>
    /// Honest up-front notice that press-and-hold remap is unverified on real hardware.
    /// The Phase-7 hardware-QA gate found the ClickHoldMode <c>0x16</c> SET frame is neither
    /// echoed nor applied by current AirPods Pro 2 firmware, while the noise-control SET over
    /// the same channel is confirmed and audible — so the command, not the transport, is at
    /// fault (issue #160). Until the frame is re-derived clean-room against current firmware,
    /// the surface must not imply the change took effect (constitution: honest surface).
    /// </summary>
    public const string ExperimentalNoticeText =
        "Experimental: current AirPods Pro 2 firmware often ignores this command. PodBridge " +
        "sends your choice, but your AirPods may keep their previous press-and-hold action. " +
        "Check on your AirPods whether it took effect.";

    /// <summary>
    /// Honest outcome text when the device echoed the gesture write. Reports the
    /// acknowledgement only — an echo is not proof the press-and-hold action actually
    /// changed, and on affected firmware this branch is not reached at all (issue #160).
    /// </summary>
    public const string AcknowledgedText = "Your AirPods acknowledged the change.";

    /// <summary>
    /// Honest outcome text when the device did not acknowledge the gesture write. States the
    /// likely cause and that PodBridge retries, without promising the retry will succeed —
    /// the earlier copy claimed the choice would simply "be re-applied the next time they
    /// reconnect", which on affected firmware never comes true (issue #160).
    /// </summary>
    public const string CouldNotConfirmText =
        "Saved, but your AirPods didn't confirm it — current firmware often ignores this " +
        "command, so it may not take effect. PodBridge will send it again the next time " +
        "they reconnect.";

    /// <summary>
    /// True when <paramref name="model"/> exposes a remappable press-and-hold gesture
    /// (ClickHoldMode <c>0x16</c>). Gated on the AirPods Pro 2 reference model — matching
    /// <see cref="NoiseControlSupport.SupportsAdaptive"/> — until the Phase-8 capability
    /// matrix broadens it; every other model hides the feature (honest, no unverified send).
    /// </summary>
    public static bool SupportsPressAndHold(AirPodsModel model) =>
        model is AirPodsModel.AirPodsPro2 or AirPodsModel.AirPodsPro2UsbC;

    /// <summary>
    /// True when <paramref name="model"/> assigns the press-and-hold action independently per
    /// bud (left vs right); false means a single shared assignment. The Pro 2 reference model
    /// advertises independent per-bud assignment, so within the Phase-7 scope this equals
    /// <see cref="SupportsPressAndHold"/> (docs/research/gesture-aap.md "per-bud addressing").
    /// </summary>
    public static bool SupportsPerBud(AirPodsModel model) => SupportsPressAndHold(model);

    /// <summary>
    /// The press-and-hold actions the model attests as settable — exactly Noise Control and
    /// Siri, the only documented action values (no invented actions) — or an empty list when
    /// the model has no remappable press-and-hold. Callers show only these, so an unsupported
    /// action can never be offered (docs/research/gesture-aap.md "action enum"; spec
    /// docs/specs/spec-gesture-remap.md).
    /// </summary>
    public static IReadOnlyList<GestureAction> AvailableActions(AirPodsModel model) =>
        SupportsPressAndHold(model)
            ? [GestureAction.NoiseControl, GestureAction.Siri]
            : [];
}
