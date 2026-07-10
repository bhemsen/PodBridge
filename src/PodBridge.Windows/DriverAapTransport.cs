using PodBridge.Core.Protocol;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Tier-2 <see cref="IAapTransport"/> over the optional KMDF L2CAP bridge driver
/// (driver/PodBridgeAAP): opens the driver's device interface, issues the connect / send
/// IOCTLs, and runs a background receive loop over the inverted-call receive IOCTL to
/// surface inbound AAP frames as <see cref="PacketReceived"/>. It is the ONLY component
/// that talks to the driver — Core and App reach the channel exclusively through this
/// adapter (docs/architecture.md, key flow 4; spec docs/specs/spec-advanced-driver-anc.md).
/// <para>
/// Graceful absence (constitution): with the driver not installed — the Tier-1 default —
/// the device interface is absent, <see cref="IsAvailable"/> is <see langword="false"/>,
/// and <see cref="ConnectAsync"/> is a no-op that never throws, so every Tier-1 feature
/// keeps working. The startup probe result is cached; a machine that installs the driver
/// must reboot for it (test-signing), which restarts the app and re-probes.
/// </para>
/// </summary>
public sealed class DriverAapTransport : IAapTransport, IDisposable
{
    private readonly IAapDriverInterop _interop;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IAapDriverChannel? _channel;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;
    private volatile bool _available;
    private bool _disposed;

    /// <summary>Production constructor: talks to the real driver via Win32.</summary>
    public DriverAapTransport()
        : this(new Win32AapDriverInterop())
    {
    }

    // Test seam: PodBridge.Windows.Tests substitutes a fake interop so the connect / send /
    // receive-loop / graceful-absence behaviour is exercised with no driver or hardware.
    internal DriverAapTransport(IAapDriverInterop interop)
    {
        ArgumentNullException.ThrowIfNull(interop);
        _interop = interop;
        _available = _interop.TryFindInterfacePath(out _); // startup probe (spec requirement)
    }

    /// <inheritdoc />
    public event EventHandler<ReadOnlyMemory<byte>>? PacketReceived;

    /// <inheritdoc />
    public event EventHandler? Connected;

    /// <inheritdoc />
    public bool IsAvailable => _available;

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var channelOpened = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is not null)
            {
                return; // idempotent: already open — not a fresh (re)connect
            }

            if (!_interop.TryFindInterfacePath(out var path) || path is null)
            {
                _available = false; // driver absent -> graceful no-op (Tier-1 keeps working)
                return;
            }

            _available = true;
            var channel = await Task.Run(() =>
            {
                var opened = _interop.Open(path);
                opened.Connect();
                return opened;
            }, cancellationToken).ConfigureAwait(false);

            _receiveCts = new CancellationTokenSource();
            var loopToken = _receiveCts.Token; // capture: the loop must not read the field
            _receiveLoop = Task.Run(() => ReceiveLoop(channel, loopToken), CancellationToken.None);
            _channel = channel;
            channelOpened = true;
        }
        finally
        {
            _gate.Release();
        }

        // Raise the Tier-2 (re)connect signal only after a fresh channel is open and the
        // gate is released — never on the idempotent already-open return — so the gesture
        // re-push policy re-applies the config Apple forgot on disconnect. Raised outside
        // the gate so a handler that writes a frame cannot re-enter under the lock.
        if (channelOpened)
        {
            Connected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopChannel();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var channel = _channel
            ?? throw new InvalidOperationException("Call ConnectAsync before SendAsync.");
        await Task.Run(() => channel.Send(packet), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the receive loop and closes the channel; safe when nothing is open.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopChannel();
        _gate.Dispose();
    }

    // Runs on a background thread: blocks on the inverted-call receive IOCTL and republishes
    // each inbound frame. A 0-length read means the request was cancelled or the channel
    // closed, which ends the loop. Handlers must not throw (they run on this thread).
    private void ReceiveLoop(IAapDriverChannel channel, CancellationToken cancellationToken)
    {
        var buffer = new byte[AapDriverNativeMethods.MaxFrameLength];
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = channel.Receive(buffer);
            if (count <= 0)
            {
                break; // cancelled / channel gone
            }

            PacketReceived?.Invoke(this, buffer.AsMemory(0, count).ToArray());
        }
    }

    // Synchronous teardown shared by DisconnectAsync and Dispose. Cancels the loop token,
    // unblocks the parked receive IOCTL, waits briefly for the loop to observe it, then
    // closes the handle. loop.Wait re-throws a faulted loop as AggregateException, which is
    // swallowed so a misbehaving inbound handler cannot break teardown.
    private void StopChannel()
    {
        var channel = _channel;
        var cts = _receiveCts;
        var loop = _receiveLoop;
        _channel = null;
        _receiveCts = null;
        _receiveLoop = null;
        if (channel is null)
        {
            return;
        }

        cts?.Cancel();
        channel.CancelPendingReceive();
        try
        {
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // A faulted receive loop (e.g. a throwing handler) must not break teardown.
        }

        channel.Dispose();
        cts?.Dispose();
    }
}
