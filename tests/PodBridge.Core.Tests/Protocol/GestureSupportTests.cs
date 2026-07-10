using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for the <see cref="GestureSupport"/> model gate: the
/// press-and-hold remap is offered only on the Phase-7 reference model (AirPods Pro 2),
/// exposes exactly Noise Control + Siri, and is hidden on every other model (honest, no
/// unverified opcode — spec docs/specs/spec-gesture-remap.md; issue #49).
/// </summary>
public class GestureSupportTests
{
    [Theory]
    [InlineData(AirPodsModel.AirPodsPro2)]
    [InlineData(AirPodsModel.AirPodsPro2UsbC)]
    public void SupportsPressAndHold_SupportedReferenceModels_True(AirPodsModel model)
        => Assert.True(GestureSupport.SupportsPressAndHold(model));

    [Theory]
    [InlineData(AirPodsModel.Unknown)]
    [InlineData(AirPodsModel.AirPods2)]
    [InlineData(AirPodsModel.AirPodsPro)]
    [InlineData(AirPodsModel.AirPods4Anc)]
    [InlineData(AirPodsModel.AirPodsPro3)]
    [InlineData(AirPodsModel.AirPodsMax)]
    public void SupportsPressAndHold_OutOfScopeModels_False(AirPodsModel model)
        => Assert.False(GestureSupport.SupportsPressAndHold(model));

    [Fact]
    public void SupportsPerBud_MirrorsPressAndHoldSupport_WithinPhase7Scope()
    {
        // The Pro 2 reference model advertises independent per-bud assignment, so per-bud
        // support equals press-and-hold support in this phase's scope.
        Assert.True(GestureSupport.SupportsPerBud(AirPodsModel.AirPodsPro2UsbC));
        Assert.False(GestureSupport.SupportsPerBud(AirPodsModel.AirPodsMax));
    }

    [Fact]
    public void AvailableActions_SupportedModel_ExposesOnlyNoiseControlAndSiri()
    {
        // Honesty gate: exactly the two documented, settable actions — no invented actions.
        var actions = GestureSupport.AvailableActions(AirPodsModel.AirPodsPro2UsbC);

        Assert.Equal([GestureAction.NoiseControl, GestureAction.Siri], actions);
    }

    [Fact]
    public void AvailableActions_UnsupportedModel_IsEmpty()
        => Assert.Empty(GestureSupport.AvailableActions(AirPodsModel.AirPodsMax));
}
