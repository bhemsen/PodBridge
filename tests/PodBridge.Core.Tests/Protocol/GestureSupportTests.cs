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

    // ---- Honesty of the user-facing copy (issue #160) --------------------------
    // The hardware-QA gate found current Pro 2 firmware neither echoes nor applies the
    // ClickHoldMode 0x16 SET, so no string may present the remap as working.

    [Fact]
    public void ExperimentalNoticeText_MarksTheFeatureExperimentalAndUnreliable()
    {
        var text = GestureSupport.ExperimentalNoticeText;

        Assert.Contains("Experimental", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ignores", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may keep", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcknowledgedText_ReportsTheEchoWithoutAssertingTheGestureChanged()
    {
        var text = GestureSupport.AcknowledgedText;

        // An echo proves the device received the frame, not that it acted on it. "Applied
        // to your AirPods." (the superseded copy) asserted the effect; this must not.
        Assert.Contains("acknowledged", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("applied", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CouldNotConfirmText_StatesTheRetryWithoutPromisingItTakesEffect()
    {
        var text = GestureSupport.CouldNotConfirmText;

        Assert.Contains("didn't confirm", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may not take effect", text, StringComparison.OrdinalIgnoreCase);

        // The superseded copy promised the choice "will be re-applied" on reconnect — an
        // outcome claim that never comes true on affected firmware. Only the send is claimed.
        Assert.DoesNotContain("re-applied", text, StringComparison.OrdinalIgnoreCase);
    }
}
