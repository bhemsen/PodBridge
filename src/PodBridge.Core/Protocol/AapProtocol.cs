using System.Diagnostics.CodeAnalysis;
using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Clean-room builders and parser for the cleartext Apple Accessory Protocol (AAP /
/// AACP) <b>noise-control</b> command over the Classic-Bluetooth L2CAP control
/// channel. Every constant below is re-stated in our own words from the documented
/// facts in <c>docs/research/aap-anc-protocol.md</c> (research issue #39) and carries
/// a citation comment (constitution: clean-room protocol). No source code or verbatim
/// protocol-doc prose is copied from any GPL/other project; only reverse-engineered
/// byte facts, cross-checked across the research sources, are re-implemented here.
/// <para>
/// This is the <b>cleartext</b> control channel only — it never touches the
/// MagicPairing-encrypted path (constitution Don'ts). Scope is limited to Phase-6
/// noise-control switching; the byte format is confirmed against the reference model
/// <b>AirPods Pro 2 (USB-C), firmware 7A305</b> and is firmware-fragile.
/// </para>
/// </summary>
public static class AapProtocol
{
    /// <summary>
    /// L2CAP PSM the AAP control channel listens on: <c>0x1001</c> (4097) — a custom
    /// Classic-BT PSM, not BLE/GATT, which is why opening it needs the Tier-2 kernel
    /// driver. (docs/research/aap-anc-protocol.md "Transport → Channel".)
    /// </summary>
    public const ushort L2capPsm = 0x1001;

    // AAP data-message header: every control frame after the handshake begins with
    // these four bytes. (docs/research/aap-anc-protocol.md "Transport → Framing".)
    private static readonly byte[] DataHeader = [0x04, 0x00, 0x04, 0x00];

    // Control/settings opcode 0x0009 written little-endian (09 00), followed by the
    // one-byte setting identifier 0x0D (ListeningMode / noise-control).
    // (docs/research/aap-anc-protocol.md "Noise-control SET packet".)
    private const byte OpcodeSettingsLow = 0x09;
    private const byte OpcodeSettingsHigh = 0x00;
    private const byte SettingIdListeningMode = 0x0D;

    // The 16-byte plaintext handshake — sent first on every connection; without it
    // the AirPods ignore all later frames. Note its distinct 00 00 04 00 01 00 …
    // prefix (NOT the 04 00 04 00 data header).
    // (docs/research/aap-anc-protocol.md "Startup sequence → 1. Handshake".)
    private static readonly byte[] Handshake =
        [0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    // "Set specific features" — unlocks the Apple-silicon-gated features so the
    // device honours an Adaptive set; without it, requesting Adaptive is echoed back
    // as a different mode. Data header + 4D 00 FF 00 then reserved zero bytes.
    // (docs/research/aap-anc-protocol.md "Startup sequence → 2. Set specific features".)
    private static readonly byte[] SetSpecificFeaturesBody =
        [0x4D, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    // "Request notifications" — subscribes to inbound state (incl. the noise-control
    // mode notification used for echo-confirm). Data header + 0F 00 FF FF FE FF.
    // (docs/research/aap-anc-protocol.md "Startup sequence → 3. Request notifications".)
    private static readonly byte[] RequestNotificationsBody =
        [0x0F, 0x00, 0xFF, 0xFF, 0xFE, 0xFF];

    // Noise-control frame: [04 00 04 00][09 00][0D][mode][00 00 00] = 11 bytes.
    // The 3 trailing zero bytes are reserved/padding.
    // (docs/research/aap-anc-protocol.md "Noise-control SET packet".)
    private const int NoiseControlFrameLength = 11;
    private const int ModeByteIndex = 7;

    /// <summary>
    /// Builds the 16-byte plaintext handshake that must be sent first on each
    /// connection (docs/research/aap-anc-protocol.md, startup step 1).
    /// </summary>
    public static byte[] BuildHandshake() => (byte[])Handshake.Clone();

    /// <summary>
    /// Builds the "set specific features" frame that unlocks Adaptive before it can be
    /// selected (docs/research/aap-anc-protocol.md, startup step 2).
    /// </summary>
    public static byte[] BuildSetSpecificFeatures() => Concat(DataHeader, SetSpecificFeaturesBody);

    /// <summary>
    /// Builds the "request notifications" frame that subscribes to inbound state,
    /// including the noise-control echo (docs/research/aap-anc-protocol.md, step 3).
    /// </summary>
    public static byte[] BuildRequestNotifications() => Concat(DataHeader, RequestNotificationsBody);

    /// <summary>
    /// Builds the 11-byte noise-control SET frame for <paramref name="mode"/>:
    /// <c>04 00 04 00 09 00 0D [mode] 00 00 00</c>. The mode byte is the
    /// <see cref="NoiseControlMode"/> value (Off=01, ANC=02, Transparency=03,
    /// Adaptive=04). (docs/research/aap-anc-protocol.md "Noise-control SET packet".)
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not a defined value.</exception>
    public static byte[] BuildSetNoiseControl(NoiseControlMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return
        [
            DataHeader[0], DataHeader[1], DataHeader[2], DataHeader[3],
            OpcodeSettingsLow, OpcodeSettingsHigh, SettingIdListeningMode,
            (byte)mode, // enum values equal the wire mode bytes 01..04
            0x00, 0x00, 0x00,
        ];
    }

    /// <summary>
    /// Parses an inbound noise-control echo / notification frame
    /// (<c>04 00 04 00 09 00 0D [mode] 00 00 00</c>) and yields its
    /// <see cref="NoiseControlMode"/>. The AirPods emit this identical layout both
    /// unsolicited (on-device change) and as the acknowledgement of a host set, so it
    /// is the confirm signal for the optimistic-set logic. Returns
    /// <see langword="false"/> for any frame that is not a well-formed noise-control
    /// notification (wrong length/header/opcode, or an unknown mode byte).
    /// (docs/research/aap-anc-protocol.md "Echo / confirmation packet".)
    /// </summary>
    public static bool TryParseNoiseControlNotification(
        ReadOnlySpan<byte> frame, [NotNullWhen(true)] out NoiseControlMode? mode)
    {
        mode = null;
        if (frame.Length != NoiseControlFrameLength)
        {
            return false;
        }

        var headerOk = frame[0] == DataHeader[0] && frame[1] == DataHeader[1]
            && frame[2] == DataHeader[2] && frame[3] == DataHeader[3];
        var opcodeOk = frame[4] == OpcodeSettingsLow && frame[5] == OpcodeSettingsHigh
            && frame[6] == SettingIdListeningMode;
        if (!headerOk || !opcodeOk)
        {
            return false;
        }

        var candidate = (NoiseControlMode)frame[ModeByteIndex];
        if (!Enum.IsDefined(candidate))
        {
            return false; // an out-of-range mode byte is not a mode we recognise
        }

        mode = candidate;
        return true;
    }

    private static byte[] Concat(byte[] header, byte[] body)
    {
        var frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, header.Length);
        return frame;
    }
}
