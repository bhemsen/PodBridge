using PodBridge.Core.Capabilities;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Capabilities;

/// <summary>
/// Device-independent tests for <see cref="CapabilityProvider"/> using the real
/// <see cref="ModelRegistry"/> + a fake <see cref="IAapTransport"/>: Tier-1 gates on the model
/// axis only (holds with the driver absent), and Tier-2 gates on driver presence AND the
/// (model, firmware-major) capability with the honest reason string in every state
/// (constitution Tier-1 test gate; spec docs/specs/spec-model-coverage-hardening.md).
/// </summary>
public class CapabilityProviderTests
{
    private static CapabilityProvider Provider(
        bool driverPresent, IReadOnlySet<FirmwareRefinement>? unsupportedFirmware = null)
        => new(new ModelRegistry(), new FakeAapTransport { IsAvailable = driverPresent }, unsupportedFirmware);

    // ---- Tier-1: model axis only, never firmware, never driver ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)] // driver absent must not change a Tier-1 answer (Tier-1 independence)
    public void Tier1_CaseBattery_GatesOnModelOnly_HoldsWithDriverAbsent(bool driverPresent)
    {
        var provider = Provider(driverPresent);

        // AirPods Max has no battery-reporting case → hidden on the model axis alone.
        Assert.False(provider.IsTier1FeatureAvailable(Tier1Feature.CaseBattery, AirPodsModel.AirPodsMax));
        // Dual-bud models report a case battery.
        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.CaseBattery, AirPodsModel.AirPodsPro2));
        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.CaseBattery, AirPodsModel.AirPods2));
    }

    [Fact]
    public void Tier1_InEarDetection_GatesOnModelOnly()
    {
        var provider = Provider(driverPresent: true);

        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.InEarDetection, AirPodsModel.AirPodsPro2));
        // AirPods Max uses head on/off detection, not in-ear.
        Assert.False(provider.IsTier1FeatureAvailable(Tier1Feature.InEarDetection, AirPodsModel.AirPodsMax));
    }

    // ---- Tier-2: driver presence AND (model, firmware-major) capability ----

    [Theory]
    [InlineData(Tier2Feature.NoiseControl)]
    [InlineData(Tier2Feature.GestureRemap)]
    public void Tier2_DriverAbsent_Off_RequiresTheOptionalDriver(Tier2Feature feature)
    {
        // Even for a model that fully supports the feature, the driver-free default is off.
        var decision = Provider(driverPresent: false)
            .GetTier2Capability(feature, AirPodsModel.AirPodsPro2, firmwareMajor: 7);

        Assert.False(decision.IsAvailable);
        Assert.Equal("requires the optional driver", decision.Reason);
    }

    [Theory]
    [InlineData(Tier2Feature.NoiseControl)]
    [InlineData(Tier2Feature.GestureRemap)]
    public void Tier2_DriverPresent_FirmwareUnreadable_FallsBackToModelLevel_StaysOn(Tier2Feature feature)
    {
        // No regression: a Phase-6/7-supported model (AirPods Pro 2) stays ON when the
        // firmware-major cannot be read (the fragile one-shot device-info push was missed).
        var decision = Provider(driverPresent: true)
            .GetTier2Capability(feature, AirPodsModel.AirPodsPro2, firmwareMajor: null);

        Assert.True(decision.IsAvailable);
        Assert.Equal("supported", decision.Reason);
    }

    [Theory]
    [InlineData(Tier2Feature.NoiseControl)]
    [InlineData(Tier2Feature.GestureRemap)]
    public void Tier2_DriverPresent_FirmwareReadable_Supported_On(Tier2Feature feature)
    {
        var decision = Provider(driverPresent: true)
            .GetTier2Capability(feature, AirPodsModel.AirPodsPro2, firmwareMajor: 7);

        Assert.True(decision.IsAvailable);
        Assert.Equal("supported", decision.Reason);
    }

    [Fact]
    public void Tier2_DriverPresent_ModelUnsupported_Off_NotSupportedOnThisModel()
    {
        var provider = Provider(driverPresent: true);

        // AirPods 2 has no ANC hardware; AirPods Max has no press-and-hold remap.
        var anc = provider.GetTier2Capability(Tier2Feature.NoiseControl, AirPodsModel.AirPods2, 7);
        var gesture = provider.GetTier2Capability(Tier2Feature.GestureRemap, AirPodsModel.AirPodsMax, 7);

        Assert.False(anc.IsAvailable);
        Assert.Equal("not supported on this model", anc.Reason);
        Assert.False(gesture.IsAvailable);
        Assert.Equal("not supported on this model", gesture.Reason);
    }

    [Fact]
    public void Tier2_DriverPresent_FirmwareMajorMarkedUnsupported_Off_NotSupportedOnThisFirmware()
    {
        var refinements = new HashSet<FirmwareRefinement>
        {
            new(Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, 99),
        };
        var provider = Provider(driverPresent: true, refinements);

        var offOnBadFirmware = provider.GetTier2Capability(
            Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, firmwareMajor: 99);
        var onOnGoodFirmware = provider.GetTier2Capability(
            Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, firmwareMajor: 7);

        Assert.False(offOnBadFirmware.IsAvailable);
        Assert.Equal("not supported on this firmware", offOnBadFirmware.Reason);
        // The refinement is narrow: other firmware-majors of the same model stay on.
        Assert.True(onOnGoodFirmware.IsAvailable);
    }

    [Fact]
    public void GracefulDegradation_DriverAbsent_Tier1Works_Tier2ReasonIsHonest()
    {
        // With the driver uninstalled: every Tier-1 feature still works (model-axis unchanged)
        // and every Tier-2 feature shows the honest "requires the optional driver" reason.
        var provider = Provider(driverPresent: false);

        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.CaseBattery, AirPodsModel.AirPodsPro2));
        Assert.True(provider.IsTier1FeatureAvailable(Tier1Feature.InEarDetection, AirPodsModel.AirPodsPro2));

        foreach (var feature in Enum.GetValues<Tier2Feature>())
        {
            var decision = provider.GetTier2Capability(feature, AirPodsModel.AirPodsPro2, firmwareMajor: null);
            Assert.False(decision.IsAvailable);
            Assert.Equal("requires the optional driver", decision.Reason);
        }
    }
}
