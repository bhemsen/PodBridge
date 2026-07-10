using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Capabilities;

/// <summary>
/// The static, in-repo (model, firmware-major) capability matrix for the Tier-2 features.
/// It answers the pure capability question — driver presence is layered on separately by
/// <see cref="CapabilityProvider"/> — from the documented facts in
/// docs/research/firmware-capabilities.md (issue #51, the sole content authority).
/// <para>
/// <b>Axes.</b> The <b>model</b> axis is well-corroborated (Apple's support matrix, the
/// AAP identifier table, MagicPods' model-keyed feature set — research sources 3, 7, 8).
/// The <b>firmware-major</b> axis is a documented <b>no-op today</b>: no source found a
/// firmware-major that toggles a whole Tier-2 feature on otherwise-identical hardware, so
/// every firmware-major of a capable model maps to that model's Phase-6/7 model-level
/// capability, and an unreadable firmware-major (the <c>0x001D</c> "Device Information"
/// push is accessory-initiated and unsolicited — there is no host read opcode — so it is a
/// fragile one-shot) falls back to the same model-level capability, never regressing shipped
/// ANC/gestures. A firmware-varying refinement is expressed as data via
/// <see cref="FirmwareRefinement"/> entries, of which the shipped set has none.
/// </para>
/// <para>
/// <b>Clean-room.</b> Model capability is derived from documented facts, not copied prose or
/// source. Gesture-remap capability delegates to the existing <see cref="GestureSupport"/>
/// gate so this matrix never claims more than Phase 7's verified reference model.
/// </para>
/// </summary>
public static class CapabilityMatrix
{
    /// <summary>
    /// Models with active-noise-control hardware (ANC / Transparency, and Adaptive where the
    /// generation supports it). Per Apple's support matrix (research source 7) this is the
    /// Pro generations and AirPods Max; AirPods 2/3 have no ANC hardware. Adaptive itself is
    /// further gated by the existing <see cref="NoiseControlSupport.SupportsAdaptive"/>; this
    /// set is the model-level gate for the noise-control feature as a whole.
    /// </summary>
    private static bool HasNoiseControl(AirPodsModel model) => model is
        AirPodsModel.AirPodsPro or
        AirPodsModel.AirPodsPro2 or AirPodsModel.AirPodsPro2UsbC or
        AirPodsModel.AirPodsPro3 or
        AirPodsModel.AirPodsMax or AirPodsModel.AirPodsMaxUsbC;

    /// <summary>
    /// Models Apple documents as supporting Conversation Awareness (research source 7):
    /// AirPods Pro 2 and Pro 3 among the vision's six models — explicitly NOT the 1st-gen
    /// Pro, Max, or the non-ANC AirPods 2/3, regardless of firmware.
    /// </summary>
    private static bool HasConversationAwareness(AirPodsModel model) => model is
        AirPodsModel.AirPodsPro2 or AirPodsModel.AirPodsPro2UsbC or
        AirPodsModel.AirPodsPro3;

    /// <summary>
    /// The model-level capability for <paramref name="feature"/> — the hardware/generation
    /// fact, before the firmware-major refinement and before the driver-presence check.
    /// Gesture-remap delegates to <see cref="GestureSupport"/> so it stays exactly consistent
    /// with the shipped Phase-7 gate (no regression, no over-claim).
    /// </summary>
    public static bool IsModelCapable(Tier2Feature feature, AirPodsModel model) => feature switch
    {
        Tier2Feature.NoiseControl => HasNoiseControl(model),
        Tier2Feature.GestureRemap => GestureSupport.SupportsPressAndHold(model),
        Tier2Feature.ConversationAwareness => HasConversationAwareness(model),
        _ => false,
    };

    /// <summary>
    /// Resolves the (model, firmware-major) capability for <paramref name="feature"/>.
    /// A model-incapable device is <see cref="Tier2Support.ModelUnsupported"/>. Otherwise the
    /// firmware-major only refines: a non-null <paramref name="firmwareMajor"/> that matches a
    /// <paramref name="unsupportedFirmware"/> entry is <see cref="Tier2Support.FirmwareUnsupported"/>;
    /// a null (unreadable) firmware-major, or one with no matching entry, falls back to the
    /// model-level capability (<see cref="Tier2Support.Supported"/>). The shipped
    /// <paramref name="unsupportedFirmware"/> set is empty, so today this equals model-only.
    /// </summary>
    public static Tier2Support Evaluate(
        Tier2Feature feature,
        AirPodsModel model,
        int? firmwareMajor,
        IReadOnlySet<FirmwareRefinement> unsupportedFirmware)
    {
        ArgumentNullException.ThrowIfNull(unsupportedFirmware);
        if (!IsModelCapable(feature, model))
        {
            return Tier2Support.ModelUnsupported;
        }

        if (firmwareMajor is int major &&
            unsupportedFirmware.Contains(new FirmwareRefinement(feature, model, major)))
        {
            return Tier2Support.FirmwareUnsupported;
        }

        return Tier2Support.Supported;
    }
}
