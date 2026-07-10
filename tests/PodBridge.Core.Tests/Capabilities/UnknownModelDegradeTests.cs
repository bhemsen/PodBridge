using PodBridge.Core.Capabilities;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Capabilities;

/// <summary>
/// Device-independent hardening tests (spec docs/specs/spec-model-coverage-hardening.md):
/// the unknown-model and driver-absent code paths degrade gracefully — never a crash, never
/// a false capability claim — through the real <see cref="ModelRegistry"/> +
/// <see cref="CapabilityProvider"/> with a fake transport.
/// </summary>
public class UnknownModelDegradeTests
{
    private static CapabilityProvider Provider(bool driverPresent)
        => new(new ModelRegistry(), new FakeAapTransport { IsAvailable = driverPresent });

    [Fact]
    public void UnknownModel_Tier1_StaysBestEffort_NeverThrows()
    {
        // The generic "Unknown AirPods" fallback keeps best-effort dual-bud Tier-1 features
        // on (battery/ear), so a brand-new or unrecognised device is never left blank.
        var provider = Provider(driverPresent: true);

        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.CaseBattery, AirPodsModel.Unknown));
        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.InEarDetection, AirPodsModel.Unknown));
    }

    [Fact]
    public void UnknownModel_Tier2_IsHonestlyOff_NeverFalselyClaimed()
    {
        // Model-specific Tier-2 features are disabled for an unknown model with an honest
        // reason, even with the driver present — never silently claimed to work.
        var provider = Provider(driverPresent: true);

        foreach (var feature in Enum.GetValues<Tier2Feature>())
        {
            var decision = provider.GetTier2Capability(feature, AirPodsModel.Unknown, firmwareMajor: null);
            Assert.False(decision.IsAvailable);
            Assert.Equal("not supported on this model", decision.Reason);
        }
    }

    [Fact]
    public void UnknownModel_DriverAbsent_Tier2_ShowsRequiresDriver_NeverThrows()
    {
        // Driver-free default with an unknown model: the honest "requires the optional
        // driver" reason takes precedence, and nothing throws.
        var provider = Provider(driverPresent: false);

        foreach (var feature in Enum.GetValues<Tier2Feature>())
        {
            var decision = provider.GetTier2Capability(feature, AirPodsModel.Unknown, firmwareMajor: 7);
            Assert.False(decision.IsAvailable);
            Assert.Equal("requires the optional driver", decision.Reason);
        }
    }

    [Fact]
    public void UnknownModel_FullPipeline_FromAdvertisement_DegradesGracefully()
    {
        // End-to-end Tier-1 path: an unrecognised Apple model id parses without throwing,
        // resolves to the labelled generic fallback, and never claims to be a known model.
        var payload = ContinuityFixtures.Proximity(status: 0x20, battery: 0x55, chargingCase: 0x05, model: 0x2099);
        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(AirPodsModel.Unknown, data.Model);

        var info = new ModelRegistry().Resolve(data.Model);
        Assert.False(info.IsRecognized);
        Assert.Equal("Unknown AirPods", info.DisplayName);
    }
}
