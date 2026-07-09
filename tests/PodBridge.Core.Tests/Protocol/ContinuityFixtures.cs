namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Builds synthetic company-id-stripped Apple-Continuity proximity-pairing payloads
/// for device-independent decode/pipeline tests (no physical AirPods).
/// </summary>
internal static class ContinuityFixtures
{
    /// <summary>
    /// Builds a 27-byte proximity-pairing (type <c>0x07</c>) block: type, length,
    /// prefix, little-endian model id, then the status/battery/charging/lid bytes.
    /// Colour, suffix, and the encrypted tail are left zero.
    /// </summary>
    public static byte[] Proximity(
        byte status,
        byte battery,
        byte chargingCase,
        byte lid = 0x00,
        ushort model = 0x200E)
    {
        var block = new byte[27];
        block[0] = 0x07;                 // message type: proximity pairing
        block[1] = 0x19;                 // remaining length 25
        block[2] = 0x01;                 // prefix constant
        block[3] = (byte)(model & 0xFF); // model id low byte (little-endian)
        block[4] = (byte)(model >> 8);   // model id high byte
        block[5] = status;
        block[6] = battery;
        block[7] = chargingCase;
        block[8] = lid;
        // block[9] colour, block[10] suffix, block[11..26] encrypted tail: left zero
        return block;
    }
}
