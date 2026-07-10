using PodBridge.Core.Capabilities;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Capabilities;

/// <summary>
/// Device-independent tests for the pure (model, firmware-major) <see cref="CapabilityMatrix"/>
/// — the model axis and the documented no-op firmware-major dimension
/// (docs/research/firmware-capabilities.md; spec docs/specs/spec-model-coverage-hardening.md).
/// </summary>
public class CapabilityMatrixTests
{
    private static readonly IReadOnlySet<FirmwareRefinement> None = new HashSet<FirmwareRefinement>();

    [Theory]
    [InlineData(AirPodsModel.AirPodsPro)]
    [InlineData(AirPodsModel.AirPodsPro2)]
    [InlineData(AirPodsModel.AirPodsPro2UsbC)]
    [InlineData(AirPodsModel.AirPodsPro3)]
    [InlineData(AirPodsModel.AirPodsMax)]
    [InlineData(AirPodsModel.AirPodsMaxUsbC)]
    public void NoiseControl_ModelCapable_ForAncHardware(AirPodsModel model)
        => Assert.True(CapabilityMatrix.IsModelCapable(Tier2Feature.NoiseControl, model));

    [Theory]
    [InlineData(AirPodsModel.AirPods2)]
    [InlineData(AirPodsModel.AirPods3)]
    [InlineData(AirPodsModel.Unknown)]
    public void NoiseControl_ModelIncapable_ForNonAncModels(AirPodsModel model)
        => Assert.False(CapabilityMatrix.IsModelCapable(Tier2Feature.NoiseControl, model));

    [Theory]
    [InlineData(AirPodsModel.AirPodsPro2)]
    [InlineData(AirPodsModel.AirPodsPro2UsbC)]
    [InlineData(AirPodsModel.AirPodsMax)]
    [InlineData(AirPodsModel.AirPods2)]
    [InlineData(AirPodsModel.Unknown)]
    public void GestureRemap_ModelCapable_DelegatesToGestureSupport(AirPodsModel model)
        => Assert.Equal(
            GestureSupport.SupportsPressAndHold(model),
            CapabilityMatrix.IsModelCapable(Tier2Feature.GestureRemap, model));

    [Theory]
    [InlineData(AirPodsModel.AirPodsPro2, true)]
    [InlineData(AirPodsModel.AirPodsPro3, true)]
    [InlineData(AirPodsModel.AirPodsPro, false)] // 1st-gen Pro: no CA per Apple (source 7)
    [InlineData(AirPodsModel.AirPodsMax, false)]
    [InlineData(AirPodsModel.AirPods2, false)]
    public void ConversationAwareness_ModelGate(AirPodsModel model, bool expected)
        => Assert.Equal(expected, CapabilityMatrix.IsModelCapable(Tier2Feature.ConversationAwareness, model));

    [Theory]
    [InlineData(null)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(99)]
    public void Evaluate_FirmwareDimension_IsNoOp_ForCapableModel(int? firmwareMajor)
    {
        // The shipped firmware-major dimension is a no-op: every firmware-major of a capable
        // model — and the unreadable (null) case — maps to the model-level capability.
        var support = CapabilityMatrix.Evaluate(
            Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, firmwareMajor, None);

        Assert.Equal(Tier2Support.Supported, support);
    }

    [Fact]
    public void Evaluate_ModelIncapable_IsModelUnsupported_RegardlessOfFirmware()
        => Assert.Equal(
            Tier2Support.ModelUnsupported,
            CapabilityMatrix.Evaluate(Tier2Feature.NoiseControl, AirPodsModel.AirPods2, 7, None));

    [Fact]
    public void Evaluate_FirmwareMajorMarkedUnsupported_IsFirmwareUnsupported()
    {
        // A future QA-confirmed refinement is a data edit; the mechanism gates off only the
        // matching (feature, model, firmware-major) and leaves other firmware-majors on.
        var refinements = new HashSet<FirmwareRefinement>
        {
            new(Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, 99),
        };

        Assert.Equal(
            Tier2Support.FirmwareUnsupported,
            CapabilityMatrix.Evaluate(Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, 99, refinements));
        Assert.Equal(
            Tier2Support.Supported,
            CapabilityMatrix.Evaluate(Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, 7, refinements));
        // Unreadable firmware-major never triggers the refinement — falls back to model level.
        Assert.Equal(
            Tier2Support.Supported,
            CapabilityMatrix.Evaluate(Tier2Feature.NoiseControl, AirPodsModel.AirPodsPro2, null, refinements));
    }
}
