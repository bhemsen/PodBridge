namespace PodBridge.Core.Protocol;

/// <summary>
/// Transport for the Apple Accessory Protocol L2CAP channel (PSM 0x1001).
/// Tier 2 only — provided by the optional kernel driver and absent by default
/// (see docs/architecture.md). Consumers must handle <see cref="IsAvailable"/>
/// being false and degrade gracefully.
/// </summary>
public interface IAapTransport
{
    /// <summary>True when the optional driver is installed and the channel is open.</summary>
    bool IsAvailable { get; }

    Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);

    event EventHandler<ReadOnlyMemory<byte>>? PacketReceived;
}
