using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Device-independent noise-control logic that drives an <see cref="IAapTransport"/>:
/// the once-per-connection AAP startup sequence plus the optimistic-set /
/// echo-confirm / timeout-revert model for switching Off / ANC / Transparency /
/// Adaptive. It builds/parses every frame through <see cref="AapProtocol"/> (Core is
/// OS-free); the transport (Tier-2 driver, issue #43) only moves raw bytes.
/// <list type="bullet">
/// <item>A set is applied <b>optimistically</b> (state + <see cref="ModeChanged"/>
/// fire immediately), the SET frame is sent, and the change is <b>confirmed</b> when
/// the AirPods echo the same mode back.</item>
/// <item>If no matching echo arrives within the confirm timeout — or the device
/// echoes a different mode (e.g. Adaptive requested without the unlock frame) — the
/// optimistic change is <b>reverted</b> and the UI told, keeping it honest.</item>
/// <item>When the transport is <b>unavailable</b> (driver absent), no frame is sent
/// and <see cref="ApplyTo"/> reports switching as disabled (graceful degradation).</item>
/// </list>
/// (docs/research/aap-anc-protocol.md; spec docs/specs/spec-advanced-driver-anc.md.)
/// </summary>
public sealed class NoiseControlController
{
    /// <summary>Default echo-confirm window before an optimistic set is reverted.</summary>
    public static readonly TimeSpan DefaultConfirmTimeout = TimeSpan.FromSeconds(2);

    private readonly IAapTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _confirmTimeout;
    private readonly Lock _gate = new();

    private NoiseControlMode? _current;

    /// <summary>
    /// Wires the controller to its transport. It does not start or open the transport;
    /// call <see cref="InitializeSessionAsync"/> once the channel is available.
    /// </summary>
    /// <param name="transport">The AAP control-channel transport (Tier-2).</param>
    /// <param name="timeProvider">Clock used for the confirm timeout; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="confirmTimeout">Echo-confirm window; defaults to <see cref="DefaultConfirmTimeout"/>.</param>
    public NoiseControlController(
        IAapTransport transport,
        TimeProvider? timeProvider = null,
        TimeSpan? confirmTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _confirmTimeout = confirmTimeout ?? DefaultConfirmTimeout;
    }

    /// <summary>Raised when the (optimistic or confirmed/reverted) current mode changes.</summary>
    public event EventHandler<NoiseControlMode?>? ModeChanged;

    /// <summary>True when the underlying transport is available (driver present).</summary>
    public bool IsAvailable => _transport.IsAvailable;

    /// <summary>The current mode, or <see langword="null"/> if unknown / not yet read.</summary>
    public NoiseControlMode? CurrentMode
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>
    /// Projects the current noise-control state onto <paramref name="state"/>. When the
    /// transport is unavailable the mode is cleared and
    /// <see cref="DeviceState.NoiseControlAvailable"/> is <see langword="false"/>, so
    /// the UI disables switching (constitution: graceful degradation).
    /// </summary>
    public DeviceState ApplyTo(DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var available = _transport.IsAvailable;
        lock (_gate)
        {
            return state with
            {
                NoiseControlAvailable = available,
                NoiseControl = available ? _current : null,
            };
        }
    }

    /// <summary>
    /// Runs the once-per-connection AAP startup sequence in the documented order —
    /// open channel → handshake → set-specific-features (Adaptive unlock) →
    /// request-notifications — so later frames are honoured and the echo notification
    /// is delivered. A no-op returning <see langword="false"/> when the transport is
    /// unavailable. (docs/research/aap-anc-protocol.md "Startup sequence".)
    /// </summary>
    public async Task<bool> InitializeSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!_transport.IsAvailable)
        {
            return false;
        }

        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        await _transport.SendAsync(AapProtocol.BuildHandshake(), cancellationToken).ConfigureAwait(false);
        await _transport.SendAsync(AapProtocol.BuildSetSpecificFeatures(), cancellationToken).ConfigureAwait(false);
        await _transport.SendAsync(AapProtocol.BuildRequestNotifications(), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Requests <paramref name="mode"/>: optimistically applies it, sends the SET frame,
    /// and returns <see cref="NoiseControlSetOutcome.Confirmed"/> on a matching echo or
    /// <see cref="NoiseControlSetOutcome.RevertedOnTimeout"/> (state rolled back) on
    /// timeout/mismatch. Sends nothing and returns
    /// <see cref="NoiseControlSetOutcome.Unavailable"/> when the transport is absent.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a defined value.</exception>
    public async Task<NoiseControlSetOutcome> SetModeAsync(
        NoiseControlMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!_transport.IsAvailable)
        {
            return NoiseControlSetOutcome.Unavailable; // graceful degrade: send nothing
        }

        NoiseControlMode? previous;
        lock (_gate)
        {
            previous = _current;
            _current = mode; // optimistic apply before the device confirms
        }

        RaiseChanged(mode);
        if (await SendAndAwaitEchoAsync(mode, cancellationToken).ConfigureAwait(false))
        {
            return NoiseControlSetOutcome.Confirmed;
        }

        lock (_gate)
        {
            _current = previous; // revert on mismatch/timeout
        }

        RaiseChanged(previous);
        return NoiseControlSetOutcome.RevertedOnTimeout;
    }

    // Sends the SET frame, then races the inbound matching-echo signal against the
    // confirm timeout. Subscribes only for the duration of the wait.
    private async Task<bool> SendAndAwaitEchoAsync(
        NoiseControlMode mode, CancellationToken cancellationToken)
    {
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPacket(object? sender, ReadOnlyMemory<byte> frame)
        {
            if (AapProtocol.TryParseNoiseControlNotification(frame.Span, out var echoed) && echoed == mode)
            {
                confirmed.TrySetResult(true);
            }
        }

        _transport.PacketReceived += OnPacket;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            await _transport.SendAsync(AapProtocol.BuildSetNoiseControl(mode), cancellationToken)
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

    private void RaiseChanged(NoiseControlMode? mode) => ModeChanged?.Invoke(this, mode);
}
