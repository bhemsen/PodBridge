namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Device-independent one-click connect / disconnect policy for already-paired AirPods,
/// driving an <see cref="IBluetoothAudioConnector"/> (Tier 1: driver-free, admin-free).
/// <list type="bullet">
/// <item>It reads the link state first: no paired AirPods → <see cref="BluetoothAudioLinkOutcome.NotFound"/>
/// (the caller falls back to Windows Bluetooth settings); already in the requested state →
/// <see cref="BluetoothAudioLinkOutcome.AlreadyInState"/>, sending nothing.</item>
/// <item>An accepted request only means the driver took it, so the outcome is
/// <b>confirmed</b> by polling the link state (every <see cref="DefaultPollInterval"/>).</item>
/// <item>A request sent while a previous link is still winding down (e.g. right after a
/// disconnect) is often silently ignored, so it is <b>re-sent</b> every
/// <see cref="DefaultResendInterval"/> until the link changes or
/// <see cref="DefaultTimeout"/> elapses (<see cref="BluetoothAudioLinkOutcome.TimedOut"/>).</item>
/// <item>Only one operation runs at a time; a concurrent call returns
/// <see cref="BluetoothAudioLinkOutcome.Busy"/> and sends nothing.</item>
/// </list>
/// The connector does short synchronous OS calls, so UI callers should invoke this off
/// the UI thread (the tray does); the waits themselves are asynchronous.
/// </summary>
public sealed class BluetoothAudioLinkController
{
    /// <summary>Default interval between link-state checks while waiting for the link to change.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Default interval after which an accepted-but-unanswered request is re-sent.</summary>
    public static readonly TimeSpan DefaultResendInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Default overall wait before giving up. Right after a disconnect the first reconnect
    /// can take well over 15 s on real AirPods, so the window is generous.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

    private readonly IBluetoothAudioConnector _connector;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _resendInterval;
    private readonly TimeSpan _timeout;
    private int _busy;

    /// <summary>Wires the controller to its connector.</summary>
    /// <param name="connector">The OS connect/disconnect boundary.</param>
    /// <param name="timeProvider">Clock for the poll/resend/timeout waits; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="pollInterval">Link-state poll interval; defaults to <see cref="DefaultPollInterval"/>.</param>
    /// <param name="resendInterval">Request re-send interval; defaults to <see cref="DefaultResendInterval"/>.</param>
    /// <param name="timeout">Overall wait; defaults to <see cref="DefaultTimeout"/>.</param>
    public BluetoothAudioLinkController(
        IBluetoothAudioConnector connector,
        TimeProvider? timeProvider = null,
        TimeSpan? pollInterval = null,
        TimeSpan? resendInterval = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(connector);
        _connector = connector;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _resendInterval = resendInterval ?? DefaultResendInterval;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>True while a connect or disconnect is in progress.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>Connects the already-paired AirPods and confirms the link came up.</summary>
    public Task<BluetoothAudioLinkOutcome> ConnectAsync(CancellationToken cancellationToken = default)
        => RunAsync(BluetoothAudioRequest.Connect, cancellationToken);

    /// <summary>Disconnects the AirPods (they stay paired) and confirms the link went down.</summary>
    public Task<BluetoothAudioLinkOutcome> DisconnectAsync(CancellationToken cancellationToken = default)
        => RunAsync(BluetoothAudioRequest.Disconnect, cancellationToken);

    private async Task<BluetoothAudioLinkOutcome> RunAsync(
        BluetoothAudioRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return BluetoothAudioLinkOutcome.Busy;
        }

        try
        {
            var target = request == BluetoothAudioRequest.Connect
                ? BluetoothAudioLinkState.Connected
                : BluetoothAudioLinkState.Disconnected;
            var state = _connector.GetLinkState();
            if (state == BluetoothAudioLinkState.NotFound)
            {
                return BluetoothAudioLinkOutcome.NotFound;
            }

            if (state == target)
            {
                return BluetoothAudioLinkOutcome.AlreadyInState;
            }

            var result = _connector.SendRequest(request);
            return result == BluetoothAudioRequestResult.Accepted
                ? await WaitForStateAsync(request, target, cancellationToken).ConfigureAwait(false)
                : ToOutcome(result);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    // Polls until the link reaches the target, re-sending the request periodically because
    // the driver silently drops a request that arrives while the old link is winding down.
    private async Task<BluetoothAudioLinkOutcome> WaitForStateAsync(
        BluetoothAudioRequest request, BluetoothAudioLinkState target, CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        var lastSent = started;
        while (true)
        {
            await Task.Delay(_pollInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            if (_connector.GetLinkState() == target)
            {
                return BluetoothAudioLinkOutcome.Succeeded;
            }

            if (_timeProvider.GetElapsedTime(started) >= _timeout)
            {
                return BluetoothAudioLinkOutcome.TimedOut;
            }

            if (_timeProvider.GetElapsedTime(lastSent) >= _resendInterval)
            {
                _ = _connector.SendRequest(request);
                lastSent = _timeProvider.GetTimestamp();
            }
        }
    }

    private static BluetoothAudioLinkOutcome ToOutcome(BluetoothAudioRequestResult result) => result switch
    {
        BluetoothAudioRequestResult.NotFound => BluetoothAudioLinkOutcome.NotFound,
        BluetoothAudioRequestResult.Unsupported => BluetoothAudioLinkOutcome.Unsupported,
        _ => BluetoothAudioLinkOutcome.Rejected,
    };
}
