using PodBridge.Core.Models;

namespace PodBridge.Core.Capabilities;

/// <summary>
/// The single Core authority that decides which features are offered, keyed by
/// <b>(model, firmware-major)</b> (spec docs/specs/spec-model-coverage-hardening.md;
/// research docs/research/firmware-capabilities.md). Device-independent and OS-free so the
/// tray / settings surfaces gate themselves without a hardware dependency.
/// <list type="bullet">
/// <item><b>Tier-1</b> (<see cref="IsTier1FeatureAvailable"/>) gates on the BLE-derived
/// <b>model axis only</b> — never firmware, never the driver — so it holds identically with
/// the driver absent (constitution: Tier-1 independence). The method deliberately takes no
/// firmware or transport argument, so it is structurally incapable of consulting them.</item>
/// <item><b>Tier-2</b> (<see cref="GetTier2Capability"/>) requires <b>both</b> driver presence
/// AND the (model, firmware-major) capability, returning an honest
/// <see cref="CapabilityDecision"/> in every state.</item>
/// </list>
/// </summary>
public interface ICapabilityProvider
{
    /// <summary>
    /// Whether the driver-free Tier-1 <paramref name="feature"/> is offered for
    /// <paramref name="model"/>, decided on the BLE-derived model shape alone (e.g. AirPods
    /// Max hides the case battery because it has no battery-reporting case). Never consults
    /// firmware or driver presence, so the answer is identical with the driver absent.
    /// </summary>
    bool IsTier1FeatureAvailable(Tier1Feature feature, AirPodsModel model);

    /// <summary>
    /// The honest Tier-2 verdict for <paramref name="feature"/> on <paramref name="model"/>
    /// with the given <paramref name="firmwareMajor"/> (<see langword="null"/> = unreadable —
    /// the fragile one-shot device-info push was missed or the driver is absent). Off with
    /// <see cref="CapabilityDecision.DriverAbsentReason"/> when the driver is absent; when the
    /// driver is present, an unreadable firmware-major falls back to the model-level capability
    /// (a Phase-6/7-supported model stays on), a firmware-major explicitly marked unsupported
    /// is off with <see cref="CapabilityDecision.FirmwareUnsupportedReason"/>, a model without
    /// the feature is off with <see cref="CapabilityDecision.ModelUnsupportedReason"/>, and
    /// otherwise on.
    /// </summary>
    CapabilityDecision GetTier2Capability(Tier2Feature feature, AirPodsModel model, int? firmwareMajor);
}
