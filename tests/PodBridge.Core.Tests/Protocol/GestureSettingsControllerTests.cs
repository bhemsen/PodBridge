using Microsoft.Extensions.Time.Testing;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for <see cref="GestureSettingsController"/> — the decision +
/// apply logic behind the gesture settings surface — driven by fake transport/store/clock:
/// the enabled-vs-degraded availability decision and selection persistence + write over the
/// transport (spec docs/specs/spec-gesture-remap.md; issue #49).
/// </summary>
public class GestureSettingsControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly GestureConfiguration Config =
        new(RightBud: GestureAction.Siri, LeftBud: GestureAction.NoiseControl);

    private static byte[] ExpectedFrame => AapProtocol.BuildSetPressAndHoldGesture(Config);

    private static GestureSettingsController CreateController(
        FakeAapTransport transport, FakeGestureConfigStore store)
    {
        var repush = new GestureRepushController(transport, store, new FakeTimeProvider(), Timeout);
        return new GestureSettingsController(transport, store, repush);
    }

    [Fact]
    public void GetAvailability_DriverAbsent_IsDriverUnavailable_EvenForASupportedModel()
    {
        // Graceful-degradation gate: with the driver absent (Tier-1 default) the surface is
        // unavailable regardless of the connected model — no packet is ever attempted.
        var controller = CreateController(
            new FakeAapTransport { IsAvailable = false }, new FakeGestureConfigStore());

        Assert.Equal(
            GestureAvailability.DriverUnavailable,
            controller.GetAvailability(AirPodsModel.AirPodsPro2UsbC));
    }

    [Fact]
    public void GetAvailability_DriverPresent_UnsupportedModel_IsModelUnsupported()
    {
        var controller = CreateController(new FakeAapTransport(), new FakeGestureConfigStore());

        Assert.Equal(
            GestureAvailability.ModelUnsupported,
            controller.GetAvailability(AirPodsModel.AirPodsMax));
    }

    [Fact]
    public void GetAvailability_DriverPresent_SupportedModel_IsAvailable()
    {
        var controller = CreateController(new FakeAapTransport(), new FakeGestureConfigStore());

        Assert.Equal(
            GestureAvailability.Available,
            controller.GetAvailability(AirPodsModel.AirPodsPro2UsbC));
    }

    [Fact]
    public void Current_ReturnsThePersistedConfiguration()
    {
        var controller = CreateController(new FakeAapTransport(), new FakeGestureConfigStore(Config));

        Assert.Equal(Config, controller.Current);
    }

    [Fact]
    public async Task ApplyAsync_Available_PersistsThenWritesTheFrame_ConfirmedOnEcho()
    {
        // Acceptance: the assignment is persisted AND written over the transport.
        var transport = new FakeAapTransport();
        var store = new FakeGestureConfigStore();
        var controller = CreateController(transport, store);

        var pending = controller.ApplyAsync(Config);
        transport.Emit(ExpectedFrame); // device echoes → the write confirms
        var outcome = await pending;

        Assert.Equal(GestureRepushOutcome.Confirmed, outcome);
        Assert.Equal(Config, store.Load()); // persisted
        Assert.Equal(ExpectedFrame, Assert.Single(transport.Sent)); // written over the transport
    }

    [Fact]
    public async Task ApplyAsync_DriverAbsent_PersistsNothing_SendsNothing_ReturnsUnavailable()
    {
        // Graceful degradation: nothing is stored or attempted when the driver is absent.
        var transport = new FakeAapTransport { IsAvailable = false };
        var store = new FakeGestureConfigStore();
        var controller = CreateController(transport, store);

        var outcome = await controller.ApplyAsync(Config);

        Assert.Equal(GestureRepushOutcome.Unavailable, outcome);
        Assert.Null(store.Load()); // nothing persisted
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task ApplyAsync_NoEcho_PersistsChoice_ReturnsCouldNotApply()
    {
        // A missing echo is non-fatal: the choice is still persisted so it re-applies on the
        // next Tier-2 (re)connect (the reconnect re-push owns the retry cadence).
        var transport = new FakeAapTransport();
        var store = new FakeGestureConfigStore();
        var time = new FakeTimeProvider();
        var repush = new GestureRepushController(transport, store, time, Timeout);
        var controller = new GestureSettingsController(transport, store, repush);

        var pending = controller.ApplyAsync(Config);
        await SpinUntilAsync(() => pending.IsCompleted, () => time.Advance(Timeout));
        var outcome = await pending;

        Assert.Equal(GestureRepushOutcome.CouldNotApply, outcome);
        Assert.Equal(Config, store.Load()); // persisted despite the miss
        Assert.Equal(2, transport.Sent.Count); // initial send + one retry (no storm)
    }

    private static async Task SpinUntilAsync(Func<bool> done, Action tick)
    {
        for (var guard = 0; !done() && guard < 100; guard++)
        {
            tick();
            await Task.Delay(10);
        }

        Assert.True(done(), "Condition was not reached before the guard limit.");
    }
}
