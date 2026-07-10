namespace PodBridge.Core.Capabilities;

/// <summary>
/// A driver-free Tier-1 feature whose availability is decided purely by the
/// BLE-derived <see cref="Models.AirPodsModel"/> shape (constitution: Tier-1
/// independence — never firmware, never driver). Passed to
/// <see cref="ICapabilityProvider.IsTier1FeatureAvailable"/>.
/// </summary>
public enum Tier1Feature
{
    /// <summary>
    /// The case battery readout — offered only for models whose case reports its own
    /// battery. AirPods Max has no battery-reporting case, so it is hidden on the model
    /// axis alone (docs/research/model-ids.md; docs/research/firmware-capabilities.md
    /// "Tier-1 gates on the BLE-derived model axis only").
    /// </summary>
    CaseBattery,

    /// <summary>
    /// In-ear detection — offered for the in-ear bud models; the over-ear AirPods Max uses
    /// head on/off detection instead (a model-axis shape fact, not a firmware fact).
    /// </summary>
    InEarDetection,
}
