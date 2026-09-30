using PodBridge.Core.Bluetooth;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Tier-1 (device-independent) tests for the one-click connect/disconnect policy: the
/// state pre-check, confirmation via the link state, the re-send of a silently ignored
/// request, the timeout, the request-result mapping, and the single in-flight guard.
/// </summary>
public class BluetoothAudioLinkControllerTests
{
    private static readonly TimeSpan Poll = BluetoothAudioLinkController.DefaultPollInterval;

    [Fact]
    public async Task Connect_AlreadyConnected_SendsNothing()
    {
        var connector = new FakeBluetoothAudioConnector { State = BluetoothAudioLinkState.Connected };
        var controller = new BluetoothAudioLinkController(connector, new TimerCountingTimeProvider());

        var outcome = await controller.ConnectAsync();

        Assert.Equal(BluetoothAudioLinkOutcome.AlreadyInState, outcome);
        Assert.Empty(connector.Requests);
    }

    [Fact]
    public async Task Connect_NoPairedAirPods_ReturnsNotFoundAndSendsNothing()
    {
        var connector = new FakeBluetoothAudioConnector { State = BluetoothAudioLinkState.NotFound };
        var controller = new BluetoothAudioLinkController(connector, new TimerCountingTimeProvider());

        var outcome = await controller.ConnectAsync();

        Assert.Equal(BluetoothAudioLinkOutcome.NotFound, outcome);
        Assert.Empty(connector.Requests);
    }

    [Fact]
    public async Task Connect_Accepted_IsConfirmedByLinkStateOnNextPoll()
    {
        var connector = new FakeBluetoothAudioConnector();
        var time = new TimerCountingTimeProvider();
        var controller = new BluetoothAudioLinkController(connector, time);

        var pending = controller.ConnectAsync();
        Assert.False(pending.IsCompleted); // accepted is not connected: it waits to confirm

        var outcome = await RunToCompletionAsync(pending, time);

        Assert.Equal(BluetoothAudioLinkOutcome.Succeeded, outcome);
        Assert.Equal([BluetoothAudioRequest.Connect], connector.Requests);
    }

    [Fact]
    public async Task Connect_FirstRequestIgnored_ResendsAfterTenSecondsAndSucceeds()
    {
        // Real hardware: right after a disconnect the first reconnect is silently dropped.
        var connector = new FakeBluetoothAudioConnector { IgnoredRequests = 1 };
        var time = new TimerCountingTimeProvider();
        var controller = new BluetoothAudioLinkController(connector, time);

        var pending = controller.ConnectAsync();
        Advance(time, pending, BluetoothAudioLinkController.DefaultResendInterval - Poll);
        Assert.False(pending.IsCompleted);
        Assert.Single(connector.Requests);

        var outcome = await RunToCompletionAsync(pending, time);

        Assert.Equal(BluetoothAudioLinkOutcome.Succeeded, outcome);
        Assert.Equal(2, connector.Requests.Count);
    }

    [Fact]
    public async Task Connect_DeviceNeverAnswers_TimesOutAfter45Seconds_ResendingEvery10()
    {
        var connector = new FakeBluetoothAudioConnector { Responds = false };
        var time = new TimerCountingTimeProvider();
        var controller = new BluetoothAudioLinkController(connector, time);

        var pending = controller.ConnectAsync();
        Advance(time, pending, BluetoothAudioLinkController.DefaultTimeout - Poll);
        Assert.False(pending.IsCompleted); // a 15 s window proved too short on hardware

        var outcome = await RunToCompletionAsync(pending, time);

        Assert.Equal(BluetoothAudioLinkOutcome.TimedOut, outcome);
        Assert.Equal(5, connector.Requests.Count); // t = 0, 10, 20, 30, 40 s
        Assert.False(controller.IsBusy);
    }

    [Theory]
    [InlineData(BluetoothAudioRequestResult.NotFound, BluetoothAudioLinkOutcome.NotFound)]
    [InlineData(BluetoothAudioRequestResult.Unsupported, BluetoothAudioLinkOutcome.Unsupported)]
    [InlineData(BluetoothAudioRequestResult.Rejected, BluetoothAudioLinkOutcome.Rejected)]
    public async Task Connect_RequestNotAccepted_ReturnsMappedOutcomeWithoutWaiting(
        BluetoothAudioRequestResult result, BluetoothAudioLinkOutcome expected)
    {
        var connector = new FakeBluetoothAudioConnector { RequestResult = result };
        var controller = new BluetoothAudioLinkController(connector, new TimerCountingTimeProvider());

        var pending = controller.ConnectAsync();

        Assert.True(pending.IsCompleted);
        Assert.Equal(expected, await pending);
        Assert.Single(connector.Requests);
    }

    [Fact]
    public async Task Disconnect_Accepted_IsConfirmedByLinkStateGoingDown()
    {
        var connector = new FakeBluetoothAudioConnector { State = BluetoothAudioLinkState.Connected };
        var time = new TimerCountingTimeProvider();
        var controller = new BluetoothAudioLinkController(connector, time);

        var outcome = await RunToCompletionAsync(controller.DisconnectAsync(), time);

        Assert.Equal(BluetoothAudioLinkOutcome.Succeeded, outcome);
        Assert.Equal([BluetoothAudioRequest.Disconnect], connector.Requests);
        Assert.Equal(BluetoothAudioLinkState.Disconnected, connector.State);
    }

    [Fact]
    public async Task Disconnect_AlreadyDisconnected_SendsNothing()
    {
        var connector = new FakeBluetoothAudioConnector();
        var controller = new BluetoothAudioLinkController(connector, new TimerCountingTimeProvider());

        var outcome = await controller.DisconnectAsync();

        Assert.Equal(BluetoothAudioLinkOutcome.AlreadyInState, outcome);
        Assert.Empty(connector.Requests);
    }

    [Fact]
    public async Task ConcurrentCall_WhileInProgress_ReturnsBusyAndSendsNothing()
    {
        var connector = new FakeBluetoothAudioConnector { Responds = false };
        var time = new TimerCountingTimeProvider();
        var controller = new BluetoothAudioLinkController(connector, time);

        var pending = controller.ConnectAsync();
        var second = await controller.DisconnectAsync();

        Assert.Equal(BluetoothAudioLinkOutcome.Busy, second);
        Assert.True(controller.IsBusy);
        Assert.Equal([BluetoothAudioRequest.Connect], connector.Requests);
        await RunToCompletionAsync(pending, time);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Cancellation_StopsWaitingAndReleasesTheBusyGuard()
    {
        var connector = new FakeBluetoothAudioConnector { Responds = false };
        var controller = new BluetoothAudioLinkController(connector, new TimerCountingTimeProvider());
        using var cts = new CancellationTokenSource();

        var pending = controller.ConnectAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public void Constructor_NullConnector_Throws()
        => Assert.Throws<ArgumentNullException>(() => new BluetoothAudioLinkController(null!));

    private static void Advance(TimerCountingTimeProvider time, Task pending, TimeSpan total)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < total; elapsed += Poll)
        {
            time.Step(pending, Poll);
        }
    }

    private static async Task<BluetoothAudioLinkOutcome> RunToCompletionAsync(
        Task<BluetoothAudioLinkOutcome> pending, TimerCountingTimeProvider time)
    {
        for (var step = 0; step < 1000 && !pending.IsCompleted; step++)
        {
            time.Step(pending, Poll);
        }

        return await pending;
    }
}
