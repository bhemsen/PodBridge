using PodBridge.Core.Audio;
using PodBridge.Core.Capabilities;
using PodBridge.Core.Models;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// Pure, deterministic assembly of a <see cref="DiagnosticsSnapshot"/> from already-read
/// facts. Takes no dependency on any OS interface — the caller
/// (<see cref="DiagnosticsSnapshotFactory"/>) does the reads; this type only shapes the
/// result, so it is trivially unit-testable with plain values (constitution Tier-1 test
/// gate: no physical device, no fake transport needed here).
/// </summary>
public static class DiagnosticsSnapshotBuilder
{
    /// <summary>
    /// Builds the snapshot. <paramref name="driverPresent"/> decides the honest
    /// <see cref="DiagnosticsSnapshot.Tier"/> / <see cref="DiagnosticsSnapshot.DriverSigningStatus"/>
    /// labels; the Tier-2 rows of <see cref="DiagnosticsSnapshot.Capabilities"/> come from
    /// <paramref name="capabilityProvider"/>, which independently re-derives driver presence
    /// from the transport it was constructed with — the two must agree in production (the
    /// factory reads both from the same <c>IAapTransport.IsAvailable</c>).
    /// </summary>
    public static DiagnosticsSnapshot Build(
        AirPodsModelInfo modelInfo,
        int? firmwareMajor,
        CodecKind codec,
        bool driverPresent,
        ICapabilityProvider capabilityProvider,
        IReadOnlyList<BleParseResult> recentBleParses)
    {
        ArgumentNullException.ThrowIfNull(modelInfo);
        ArgumentNullException.ThrowIfNull(capabilityProvider);
        ArgumentNullException.ThrowIfNull(recentBleParses);

        return new DiagnosticsSnapshot
        {
            Model = modelInfo.Model,
            ModelDisplayName = modelInfo.DisplayName,
            FirmwareMajor = firmwareMajor,
            Codec = codec,
            Tier = driverPresent ? "Tier 2 (advanced tier, driver present)" : "Tier 1 (driver-free)",
            DriverPresent = driverPresent,
            DriverSigningStatus = driverPresent
                ? Diagnostics.DriverSigningStatus.TestSignedDriverPresent
                : Diagnostics.DriverSigningStatus.NoDriverInstalled,
            Capabilities = BuildCapabilityMatrix(modelInfo.Model, firmwareMajor, capabilityProvider),
            RecentBleParses = [.. recentBleParses],
        };
    }

    private static List<CapabilityMatrixEntry> BuildCapabilityMatrix(
        AirPodsModel model, int? firmwareMajor, ICapabilityProvider capabilityProvider)
    {
        var entries = new List<CapabilityMatrixEntry>();
        foreach (var feature in Enum.GetValues<Tier1Feature>())
        {
            var available = capabilityProvider.IsTier1FeatureAvailable(feature, model);
            entries.Add(new CapabilityMatrixEntry
            {
                Feature = $"Tier1.{feature}",
                IsAvailable = available,
                Reason = available ? CapabilityDecision.SupportedReason : CapabilityDecision.ModelUnsupportedReason,
            });
        }

        foreach (var feature in Enum.GetValues<Tier2Feature>())
        {
            var decision = capabilityProvider.GetTier2Capability(feature, model, firmwareMajor);
            entries.Add(new CapabilityMatrixEntry
            {
                Feature = $"Tier2.{feature}",
                IsAvailable = decision.IsAvailable,
                Reason = decision.Reason,
            });
        }

        return entries;
    }
}
