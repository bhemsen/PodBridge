namespace PodBridge.Core.Protocol;

/// <summary>
/// The Core-side boundary for the Apple Accessory Protocol (AAP) control channel:
/// the cleartext Classic-Bluetooth <b>L2CAP</b> link on <b>PSM 0x1001</b>
/// (see <see cref="AapProtocol"/> and docs/research/aap-anc-protocol.md). User-mode
/// Windows cannot open that PSM, so this is a Tier-2-only capability supplied by the
/// optional kernel driver (implemented by <c>DriverAapTransport</c> in
/// <c>PodBridge.Windows</c>, issue #43) and absent by default. Core stays OS-free:
/// this is the interface only — no P/Invoke, no WinRT.
/// <para>
/// Consumers MUST treat <see cref="IsAvailable"/> being <see langword="false"/> as
/// the normal driver-free state and degrade gracefully — never assume the channel
/// exists (constitution: graceful degradation).
/// </para>
/// </summary>
public interface IAapTransport
{
    /// <summary>
    /// <see langword="true"/> only when the optional driver is installed and the
    /// L2CAP channel can be used. <see langword="false"/> in the driver-free Tier-1
    /// default; callers disable AAP features rather than crash.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens the L2CAP channel to the connected AirPods (Tier-2 driver work). A no-op
    /// or fast return is acceptable when the channel is already open; implementations
    /// that are <see cref="IsAvailable"/>-false may throw or return without opening.
    /// The AAP session handshake itself is sent as frames via <see cref="SendAsync"/>
    /// (see <see cref="NoiseControlController"/>), not by this method.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the L2CAP channel; safe to call when already closed.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one raw AAP frame (built by <see cref="AapProtocol"/>) to the channel.
    /// The transport does not interpret the bytes.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised for every inbound raw AAP frame (e.g. the noise-control echo /
    /// notification that confirms a mode change). The payload is the frame bytes;
    /// the transport does not interpret them.
    /// </summary>
    event EventHandler<ReadOnlyMemory<byte>>? PacketReceived;

    /// <summary>
    /// Raised each time the Tier-2 L2CAP channel is (re)connected — i.e. every time
    /// <see cref="ConnectAsync"/> opens a fresh channel (not on the idempotent
    /// already-open return). It is the OS-free signal the gesture re-push policy
    /// subscribes to: Apple firmware forgets a third-party host's control-command
    /// config across a disconnect, so the stored configuration must be re-sent on every
    /// (re)connect (docs/research/gesture-aap.md "reconnect-overwrite"). It lives on this
    /// abstraction — not the concrete <c>DriverAapTransport</c> — so Core stays OS-free
    /// and a fake transport can fire it device-independently in tests.
    /// <para>
    /// Handlers must not assume the AAP session handshake has completed when this fires;
    /// it marks the transport-level (re)connect. A consumer that needs the handshake
    /// (e.g. the gesture re-push) relies on the write+echo-confirm-with-retry pattern to
    /// absorb a frame that races the once-per-connection handshake.
    /// </para>
    /// </summary>
    event EventHandler? Connected;
}
