using Microsoft.Extensions.Time.Testing;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for <see cref="GestureRepushController"/> driven by a fake
/// transport + store + clock: re-push on the (re)connect signal, re-reading the store each
/// time, the write+echo confirm with a single retry (no storm), and driver-absent /
/// unconfigured graceful no-ops (spec docs/specs/spec-gesture-remap.md; issue #48).
/// </summary>
public class GestureRepushControllerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly GestureConfiguration Config =
        new(RightBud: GestureAction.NoiseControl, LeftBud: GestureAction.Siri);

    private static byte[] ExpectedFrame => AapProtocol.BuildSetPressAndHoldGesture(Config);

    [Fact]
    public void Connected_RePushesTheStoredConfiguration()
    {
        // Acceptance: firing the IAapTransport (re)connect event re-pushes the stored config.
        var transport = new FakeAapTransport();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);

        transport.RaiseConnected();
        transport.Emit(ExpectedFrame); // device echoes → the pending re-push confirms

        Assert.Equal(ExpectedFrame, Assert.Single(transport.Sent));
    }

    [Fact]
    public async Task RepushAsync_ConfirmedOnMatchingEcho()
    {
        var transport = new FakeAapTransport();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);

        var pending = controller.RepushAsync();
        transport.Emit(ExpectedFrame); // device echo
        var outcome = await pending;

        Assert.Equal(GestureRepushOutcome.Confirmed, outcome);
        Assert.Equal(ExpectedFrame, Assert.Single(transport.Sent)); // sent exactly once
    }

    [Fact]
    public async Task RepushAsync_NoConfiguration_SendsNothing()
    {
        var transport = new FakeAapTransport();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(configuration: null), new FakeTimeProvider(), Timeout);

        var outcome = await controller.RepushAsync();

        Assert.Equal(GestureRepushOutcome.NoConfiguration, outcome);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task RepushAsync_TransportUnavailable_SendsNothing()
    {
        var transport = new FakeAapTransport { IsAvailable = false };
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);

        var outcome = await controller.RepushAsync();

        Assert.Equal(GestureRepushOutcome.Unavailable, outcome);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task RepushAsync_NoEcho_RetriesOnceThenCouldNotApply()
    {
        // A missing echo must yield a non-fatal "couldn't apply" after ONE retry — no storm.
        var transport = new FakeAapTransport();
        var time = new FakeTimeProvider();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), time, Timeout);

        var pending = controller.RepushAsync();
        await ElapseBothConfirmWindowsAsync(time, pending);
        var outcome = await pending;

        Assert.Equal(GestureRepushOutcome.CouldNotApply, outcome);
        Assert.Equal(2, transport.Sent.Count); // initial send + exactly one retry
    }

    [Fact]
    public async Task RepushAsync_TransportThrowsOnSend_CouldNotApply()
    {
        // A closed/broken Tier-2 channel makes DriverAapTransport.SendAsync throw (e.g.
        // "Call ConnectAsync before SendAsync." when the L2CAP channel isn't open). That must
        // be a non-fatal miss, honouring the documented never-throws invariant — not an escape
        // that would reach the WPF dispatcher via the awaited Apply handler and crash the tray.
        var transport = new FakeAapTransport
        {
            SendException = new InvalidOperationException("Call ConnectAsync before SendAsync."),
        };
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);

        var outcome = await controller.RepushAsync();

        Assert.Equal(GestureRepushOutcome.CouldNotApply, outcome);
    }

    [Fact]
    public async Task RepushAsync_RetryConfirms_WhenTheSecondAttemptIsEchoed()
    {
        // The first attempt times out; the retry is echoed and confirms.
        var transport = new FakeAapTransport();
        var time = new FakeTimeProvider();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), time, Timeout);

        var pending = controller.RepushAsync();
        await SpinUntilAsync(() => transport.Sent.Count >= 2, () => time.Advance(Timeout));
        transport.Emit(ExpectedFrame); // echo the retry
        var outcome = await pending;

        Assert.Equal(GestureRepushOutcome.Confirmed, outcome);
        Assert.Equal(2, transport.Sent.Count);
    }

    [Fact]
    public void Connected_RePushesOnEveryReconnect_ReReadingTheStore()
    {
        // The config is re-pushed on EVERY (re)connect and re-read each time, so a value the
        // user changed between reconnects is what gets applied (Apple forgets it each drop).
        var transport = new FakeAapTransport();
        var store = new FakeGestureConfigStore(Config);
        using var controller = new GestureRepushController(
            transport, store, new FakeTimeProvider(), Timeout);

        transport.RaiseConnected();
        transport.Emit(ExpectedFrame);
        transport.RaiseConnected();
        transport.Emit(ExpectedFrame);

        Assert.Equal(2, store.LoadCount);
        Assert.Equal(2, transport.Sent.Count);
    }

    [Fact]
    public async Task Repushed_EventReportsTheOutcome()
    {
        var transport = new FakeAapTransport();
        using var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);
        var outcomes = new List<GestureRepushOutcome>();
        controller.Repushed += (_, o) => outcomes.Add(o);

        var pending = controller.RepushAsync();
        transport.Emit(ExpectedFrame);
        await pending;

        Assert.Equal([GestureRepushOutcome.Confirmed], outcomes);
    }

    [Fact]
    public void Dispose_UnsubscribesSoALateReconnectSendsNothing()
    {
        var transport = new FakeAapTransport();
        var controller = new GestureRepushController(
            transport, new FakeGestureConfigStore(Config), new FakeTimeProvider(), Timeout);

        controller.Dispose();
        transport.RaiseConnected();

        Assert.Empty(transport.Sent);
    }

    // Advances the fake clock until the re-push task completes, letting the threadpool
    // continuation between the initial attempt and its single retry run in between.
    private static Task ElapseBothConfirmWindowsAsync(FakeTimeProvider time, Task pending)
        => SpinUntilAsync(() => pending.IsCompleted, () => time.Advance(Timeout));

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
