using Microsoft.Extensions.Time.Testing;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for <see cref="NoiseControlController"/> driven by a fake
/// transport + a fake clock: startup-frame ordering, optimistic-set /
/// echo-confirm / timeout-revert, and driver-absent graceful degradation
/// (constitution Tier-1 test gate; docs/research/aap-anc-protocol.md).
/// </summary>
public class NoiseControlControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task InitializeSession_SendsHandshakeFeaturesNotifications_InOrder()
    {
        var transport = new FakeAapTransport();
        var controller = new NoiseControlController(transport);

        var ok = await controller.InitializeSessionAsync();

        Assert.True(ok);
        Assert.True(transport.Connected);
        Assert.Equal(3, transport.Sent.Count);
        Assert.Equal(AapProtocol.BuildHandshake(), transport.Sent[0]);
        Assert.Equal(AapProtocol.BuildSetSpecificFeatures(), transport.Sent[1]);
        Assert.Equal(AapProtocol.BuildRequestNotifications(), transport.Sent[2]);
    }

    [Fact]
    public async Task InitializeSession_Unavailable_SendsNothing()
    {
        var transport = new FakeAapTransport { IsAvailable = false };
        var controller = new NoiseControlController(transport);

        var ok = await controller.InitializeSessionAsync();

        Assert.False(ok);
        Assert.False(transport.Connected);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task SetMode_ConfirmedOnMatchingEcho()
    {
        var transport = new FakeAapTransport();
        var controller = new NoiseControlController(transport, new FakeTimeProvider(), Timeout);
        var changes = new List<NoiseControlMode?>();
        controller.ModeChanged += (_, m) => changes.Add(m);

        var pending = controller.SetModeAsync(NoiseControlMode.NoiseCancellation);
        transport.Emit(AapProtocol.BuildSetNoiseControl(NoiseControlMode.NoiseCancellation)); // device echo
        var outcome = await pending;

        Assert.Equal(NoiseControlSetOutcome.Confirmed, outcome);
        Assert.Equal(NoiseControlMode.NoiseCancellation, controller.CurrentMode);
        Assert.Equal(AapProtocol.BuildSetNoiseControl(NoiseControlMode.NoiseCancellation), Assert.Single(transport.Sent));
        Assert.Equal([NoiseControlMode.NoiseCancellation], changes); // optimistic only, no revert
    }

    [Fact]
    public async Task SetMode_RevertedOnTimeout_RollsBackToPreviousMode()
    {
        var transport = new FakeAapTransport();
        var time = new FakeTimeProvider();
        var controller = new NoiseControlController(transport, time, Timeout);

        // Establish a confirmed previous mode (ANC), then request Transparency and
        // let the confirm window elapse with no echo.
        var first = controller.SetModeAsync(NoiseControlMode.NoiseCancellation);
        transport.Emit(AapProtocol.BuildSetNoiseControl(NoiseControlMode.NoiseCancellation));
        await first;

        var changes = new List<NoiseControlMode?>();
        controller.ModeChanged += (_, m) => changes.Add(m);
        var pending = controller.SetModeAsync(NoiseControlMode.Transparency);
        time.Advance(Timeout); // no echo arrives → confirm window elapses
        var outcome = await pending;

        Assert.Equal(NoiseControlSetOutcome.RevertedOnTimeout, outcome);
        Assert.Equal(NoiseControlMode.NoiseCancellation, controller.CurrentMode);
        Assert.Equal([NoiseControlMode.Transparency, NoiseControlMode.NoiseCancellation], changes);
    }

    [Fact]
    public async Task SetMode_MismatchedEcho_RevertsAfterTimeout()
    {
        // Adaptive caveat: without the unlock frame the device echoes a different mode;
        // the mismatch does not confirm and the optimistic change reverts on timeout.
        var transport = new FakeAapTransport();
        var time = new FakeTimeProvider();
        var controller = new NoiseControlController(transport, time, Timeout);

        var pending = controller.SetModeAsync(NoiseControlMode.Adaptive);
        transport.Emit(AapProtocol.BuildSetNoiseControl(NoiseControlMode.NoiseCancellation)); // wrong mode echo
        time.Advance(Timeout);
        var outcome = await pending;

        Assert.Equal(NoiseControlSetOutcome.RevertedOnTimeout, outcome);
        Assert.Null(controller.CurrentMode); // reverted to the unknown baseline
        Assert.Equal(AapProtocol.BuildSetNoiseControl(NoiseControlMode.Adaptive), Assert.Single(transport.Sent));
    }

    [Fact]
    public async Task SetMode_TransportUnavailable_SendsNothingAndDisablesInDeviceState()
    {
        var transport = new FakeAapTransport { IsAvailable = false };
        var controller = new NoiseControlController(transport);
        var changes = new List<NoiseControlMode?>();
        controller.ModeChanged += (_, m) => changes.Add(m);

        var outcome = await controller.SetModeAsync(NoiseControlMode.NoiseCancellation);
        var state = controller.ApplyTo(DeviceState.Unknown);

        Assert.Equal(NoiseControlSetOutcome.Unavailable, outcome);
        Assert.Empty(transport.Sent);
        Assert.Empty(changes);
        Assert.Null(controller.CurrentMode);
        Assert.False(state.NoiseControlAvailable);
        Assert.Null(state.NoiseControl);
    }

    [Fact]
    public async Task ApplyTo_Available_ProjectsConfirmedMode()
    {
        var transport = new FakeAapTransport();
        var controller = new NoiseControlController(transport, new FakeTimeProvider(), Timeout);
        var pending = controller.SetModeAsync(NoiseControlMode.Transparency);
        transport.Emit(AapProtocol.BuildSetNoiseControl(NoiseControlMode.Transparency));
        await pending;

        var state = controller.ApplyTo(DeviceState.Unknown);

        Assert.True(state.NoiseControlAvailable);
        Assert.Equal(NoiseControlMode.Transparency, state.NoiseControl);
    }
}
