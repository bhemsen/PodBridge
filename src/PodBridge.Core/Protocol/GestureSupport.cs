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
