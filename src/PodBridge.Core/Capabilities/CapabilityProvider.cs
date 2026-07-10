using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.Core.Capabilities;

/// <summary>
/// Default <see cref="ICapabilityProvider"/>. Tier-1 gates on the <see cref="IModelRegistry"/>
/// model shape only; Tier-2 combines driver presence (via <see cref="IAapTransport.IsAvailable"/>)
/// with the <see cref="CapabilityMatrix"/> (model + firmware-major) capability into an honest
/// <see cref="CapabilityDecision"/>. Core stays OS-free: it depends only on Core abstractions;
/// the concrete transport/registry are wired at the composition root.
/// </summary>
public sealed class CapabilityProvider : ICapabilityProvider
{
    private readonly IModelRegistry _registry;
    private readonly IAapTransport _transport;
    private readonly IReadOnlySet<FirmwareRefinement> _unsupportedFirmware;

    /// <summary>
    /// Wires the provider to the model registry (Tier-1 shape + the model axis) and the Tier-2
    /// transport (driver presence). <paramref name="unsupportedFirmware"/> is the set of
    /// firmware-major refinements that gate a feature off; it defaults to <b>empty</b> — the
    /// shipped no-op firmware dimension (docs/research/firmware-capabilities.md) — and is a
    /// seam so a future QA-confirmed refinement is a data edit, not a code change.
    /// </summary>
    public CapabilityProvider(
        IModelRegistry registry,
        IAapTransport transport,
        IReadOnlySet<FirmwareRefinement>? unsupportedFirmware = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(transport);
        _registry = registry;
        _transport = transport;
        _unsupportedFirmware = unsupportedFirmware ?? EmptySet;
    }

    private static readonly IReadOnlySet<FirmwareRefinement> EmptySet =
        new HashSet<FirmwareRefinement>();

    /// <inheritdoc/>
    public bool IsTier1FeatureAvailable(Tier1Feature feature, AirPodsModel model)
    {
        // Model axis only — never firmware, never _transport (Tier-1 independence).
        var shape = _registry.Resolve(model);
        return feature switch
        {
            Tier1Feature.CaseBattery => shape.HasBatteryCase,
            Tier1Feature.InEarDetection => shape.HasInEarDetection,
            _ => false,
        };
    }

    /// <inheritdoc/>
    public CapabilityDecision GetTier2Capability(
        Tier2Feature feature, AirPodsModel model, int? firmwareMajor)
    {
        if (!_transport.IsAvailable)
        {
            return CapabilityDecision.DriverAbsent; // Tier-1 default: never claim it works
        }

        return CapabilityMatrix.Evaluate(feature, model, firmwareMajor, _unsupportedFirmware) switch
        {
            Tier2Support.Supported => CapabilityDecision.Available,
            Tier2Support.FirmwareUnsupported => CapabilityDecision.FirmwareUnsupported,
            _ => CapabilityDecision.ModelUnsupported,
        };
    }
}
