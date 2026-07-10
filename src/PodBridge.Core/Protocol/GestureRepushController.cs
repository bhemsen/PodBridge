using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Device-independent re-push policy for the press-and-hold gesture configuration. Apple
/// firmware forgets a third-party host's control-command config across a disconnect, so the
/// stored <see cref="GestureConfiguration"/> must be re-sent on <b>every</b> Tier-2
/// (re)connect (docs/research/gesture-aap.md "reconnect-overwrite"; spec
/// docs/specs/spec-gesture-remap.md). This controller subscribes to the OS-free
/// <see cref="IAapTransport.Connected"/> signal and, on each (re)connect, reloads the
/// persisted config from <see cref="IGestureConfigStore"/> and re-writes it over the
/// transport, confirming with the Phase-6 write+echo pattern.
/// <list type="bullet">
/// <item>Nothing stored → <see cref="GestureRepushOutcome.NoConfiguration"/> (an unconfigured
/// device is never handed an unsolicited assignment).</item>
/// <item>Transport unavailable (driver absent — the Tier-1 default) →
/// <see cref="GestureRepushOutcome.Unavailable"/>; no frame is sent (graceful degradation).</item>
/// <item>No matching echo after the send and a <b>single</b> retry →
/// <see cref="GestureRepushOutcome.CouldNotApply"/> (non-fatal, no retry storm; the next
/// (re)connect tries again).</item>
/// </list>
/// Core is OS-free: every frame is built/parsed through <see cref="AapProtocol"/> and the
/// transport only moves raw bytes.
/// </summary>
public sealed class GestureRepushController : IDisposable
{
    /// <summary>Default echo-confirm window for one re-push attempt.</summary>
    public static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(2);

    private readonly IAapTransport _transport;
    private readonly IGestureConfigStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _confirmTimeout;

    /// <summary>
    /// Wires the policy to its transport and store and starts listening for
    /// <see cref="IAapTransport.Connected"/>. No frame is sent until the transport
    /// (re)connects (or <see cref="RepushAsync"/> is called explicitly).
    /// </summary>
    /// <param name="transport">The AAP control-channel transport (Tier-2).</param>
    /// <param name="store">The persisted gesture-configuration store.</param>
    /// <param name="timeProvider">Clock for the confirm timeout; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="confirmTimeout">Echo-confirm window; defaults to <see cref="DefaultConfirmTimeout"/>.</param>
    public GestureRepushController(
        IAapTransport transport,
        IGestureConfigStore store,
        TimeProvider? timeProvider = null,
        TimeSpan? confirmTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(store);
        _transport = transport;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _confirmTimeout = confirmTimeout ?? DefaultConfirmTimeout;
        _transport.Connected += OnConnected;
    }

    /// <summary>Raised after each re-push attempt with its <see cref="GestureRepushOutcome"/>.</summary>
    public event EventHandler<GestureRepushOutcome>? Repushed;

    /// <summary>
    /// Re-sends the persisted gesture configuration and confirms it via the AirPods echo,
    /// retrying <b>once</b> on a miss. Sends nothing when the transport is unavailable or no
    /// configuration is stored. Called automatically on <see cref="IAapTransport.Connected"/>;
    /// also callable directly (e.g. right after the user changes the assignment).
    /// </summary>
    public async Task<GestureRepushOutcome> RepushAsync(CancellationToken cancellationToken = default)
    {
        if (!_transport.IsAvailable)
        {
            return Report(GestureRepushOutcome.Unavailable); // graceful degrade: send nothing
        }

        var configuration = _store.Load();
        if (configuration is null)
        {
            return Report(GestureRepushOutcome.NoConfiguration);
        }

        // Initial send + a single retry (spec: one retry, no storm). The retry also absorbs
        // a first frame that raced the once-per-connection AAP handshake on a fresh connect.
        if (await SendAndAwaitEchoAsync(configuration, cancellationToken).ConfigureAwait(false)
            || await SendAndAwaitEchoAsync(configuration, cancellationToken).ConfigureAwait(false))
        {
            return Report(GestureRepushOutcome.Confirmed);
        }

        return Report(GestureRepushOutcome.CouldNotApply);
    }

    /// <summary>Stops listening for (re)connects so no late event fires after teardown.</summary>
    public void Dispose() => _transport.Connected -= OnConnected;

    // Fire-and-forget from the transport's (re)connect signal (may run on its background
    // receive/connect thread). RepushAsync never throws — every path returns an outcome.
    private void OnConnected(object? sender, EventArgs e) => _ = RepushAsync();

    // Sends the gesture SET frame, then races the inbound matching-echo signal against the
    // confirm timeout. Subscribes only for the duration of the wait (mirrors the Phase-6
    // noise-control confirm pattern).
    private async Task<bool> SendAndAwaitEchoAsync(
        GestureConfiguration configuration, CancellationToken cancellationToken)
    {
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPacket(object? sender, ReadOnlyMemory<byte> frame)
        {
            if (AapProtocol.TryParsePressAndHoldGestureNotification(frame.Span, out var echoed)
                && echoed == configuration)
            {
                confirmed.TrySetResult(true);
            }
        }

        _transport.PacketReceived += OnPacket;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await _transport.SendAsync(
                AapProtocol.BuildSetPressAndHoldGesture(configuration), cancellationToken)
                .ConfigureAwait(false);
            var timeout = Task.Delay(_confirmTimeout, _timeProvider, timeoutCts.Token);
            var winner = await Task.WhenAny(confirmed.Task, timeout).ConfigureAwait(false);
            return winner == confirmed.Task;
        }
        finally
        {
            _transport.PacketReceived -= OnPacket;
            timeoutCts.Cancel(); // stop the pending timer whichever side won
        }
    }

    private GestureRepushOutcome Report(GestureRepushOutcome outcome)
    {
        Repushed?.Invoke(this, outcome);
        return outcome;
    }
}
