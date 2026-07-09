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

    /// <summary>Frames written via <see cref="SendAsync"/>, in order.</summary>
    public IReadOnlyList<byte[]> Sent => _sent;

    public bool Connected { get; private set; }

    public event EventHandler<ReadOnlyMemory<byte>>? PacketReceived;

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

    public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        _sent.Add(packet.ToArray());
        return Task.CompletedTask;
    }

    /// <summary>Simulate an inbound AAP frame arriving from the AirPods.</summary>
    public void Emit(byte[] frame) => PacketReceived?.Invoke(this, frame);
}
