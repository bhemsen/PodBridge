using PodBridge.Core.Protocol;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent stand-in for the Tier-2 AAP transport (the real one is the
/// KMDF-driver-backed <c>DriverAapTransport</c>, issue #43). Records every sent frame,
/// lets a test flip <see cref="IsAvailable"/> to simulate the driver being absent, and
/// lets a test push an inbound echo/notification frame via <see cref="Emit"/> — so the
/// constitution Tier-1 test gate can exercise the noise-control logic with no hardware.
/// </summary>
internal sealed class FakeAapTransport : IAapTransport
{
    private readonly List<byte[]> _sent = [];

    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// When set, <see cref="SendAsync"/> faults with it instead of recording the frame —
    /// simulates a closed/broken Tier-2 channel (the real <c>DriverAapTransport</c> throws
    /// e.g. <see cref="InvalidOperationException"/> "Call ConnectAsync before SendAsync.").
    /// </summary>
    public Exception? SendException { get; set; }

    /// <summary>Frames written via <see cref="SendAsync"/>, in order.</summary>
    public IReadOnlyList<byte[]> Sent => _sent;

    public bool Connected { get; private set; }

    public event EventHandler<ReadOnlyMemory<byte>>? PacketReceived;

    /// <summary>The OS-free (re)connect signal the gesture re-push policy subscribes to.</summary>
    public event EventHandler? ConnectedSignal;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Connected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        Connected = false;
        return Task.CompletedTask;
    }

    // Explicit interface implementation: the IAapTransport (re)connect event is surfaced
    // via ConnectedSignal + RaiseConnected so tests can fire it device-independently without
    // colliding with the existing bool Connected flag the noise-control tests assert on.
    event EventHandler? IAapTransport.Connected
    {
        add => ConnectedSignal += value;
        remove => ConnectedSignal -= value;
    }

    /// <summary>Simulate a Tier-2 (re)connect: fire the IAapTransport.Connected event.</summary>
    public void RaiseConnected() => ConnectedSignal?.Invoke(this, EventArgs.Empty);

    public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        if (SendException is not null)
        {
            return Task.FromException(SendException);
        }

        _sent.Add(packet.ToArray());
        return Task.CompletedTask;
    }

    /// <summary>Simulate an inbound AAP frame arriving from the AirPods.</summary>
    public void Emit(byte[] frame) => PacketReceived?.Invoke(this, frame);
}
