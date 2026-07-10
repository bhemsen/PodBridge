using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for the Adaptive model gate (issue #44 acceptance:
/// "Adaptive is gated on model support"). Phase 6 offers Adaptive on the AirPods Pro 2
/// reference model only; the broad matrix is Phase 8
/// (docs/research/aap-anc-protocol.md "Model / firmware support").
/// </summary>
public class NoiseControlSupportTests
{
    [Theory]
    [InlineData(AirPodsModel.AirPodsPro2)] // 0x2014
    [InlineData(AirPodsModel.AirPodsPro2UsbC)] // 0x2024 — the Phase-6 reference model
    public void SupportsAdaptive_True_ForPro2Family(AirPodsModel model)
        => Assert.True(NoiseControlSupport.SupportsAdaptive(model));

    [Theory]
    [InlineData(AirPodsModel.Unknown)] // no device / unidentified → Adaptive stays gated off
    [InlineData(AirPodsModel.AirPodsPro)] // 1st-gen Pro: Off/ANC/Transparency only (Apple matrix)
    [InlineData(AirPodsModel.AirPodsMax)] // Max: Off/ANC/Transparency only (Apple matrix)
    [InlineData(AirPodsModel.AirPodsMaxUsbC)]
    [InlineData(AirPodsModel.AirPods2)] // no ANC hardware at all
    [InlineData(AirPodsModel.AirPods3)]
    public void SupportsAdaptive_False_ForNonPro2Models(AirPodsModel model)
        => Assert.False(NoiseControlSupport.SupportsAdaptive(model));
}
