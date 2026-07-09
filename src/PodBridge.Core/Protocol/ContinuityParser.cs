using System.Diagnostics.CodeAnalysis;
using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Clean-room decoder for the cleartext Apple-Continuity <b>proximity-pairing</b>
/// (type <c>0x07</c>) BLE advertisement — the driver-free Tier-1 telemetry path.
/// Every offset, bit position and constant below is annotated with the documented
/// fact it derives from in <c>docs/research/continuity-parser.md</c> (constitution:
/// clean-room protocol). No GPL source or verbatim doc prose is reproduced; only
/// reverse-engineered facts (byte layout, bit semantics) cross-checked across the
/// research sources are re-implemented here.
/// <para>
/// This decodes the <b>cleartext</b> nibbles only. Offsets 11–26 (the encrypted /
/// hashed tail) are never read — decrypting them would require the pairing key and
/// is out of scope (constitution: never defeat MagicPairing encryption).
/// </para>
/// </summary>
public static class ContinuityParser
{
    // Manufacturer-data framing: Continuity TLVs are [type][length][value…]; the
    // proximity-pairing entry is type 0x07, value length 0x19 (25) → a 27-byte block.
    // (docs/research/continuity-parser.md "Manufacturer-data framing".)
    private const byte ProximityType = 0x07;
    private const byte ProximityValueLength = 0x19; // 25
    private const int ProximityBlockLength = 27;    // type + length + 25 value bytes

    // Byte offsets within the block (offset 0 = the 0x07 type byte).
    // (docs/research/continuity-parser.md "Byte-offset table".)
    private const int ModelLowOffset = 3;      // model id, little-endian low byte
    private const int ModelHighOffset = 4;     // model id, little-endian high byte
    private const int StatusOffset = 5;        // in-ear / in-case / primary-side flags
    private const int BatteryOffset = 6;       // low nibble = primary pod, high = secondary
    private const int ChargingCaseOffset = 7;  // low nibble = case battery, high nibble = charging bits
    private const int LidOffset = 8;           // open/close counter + lid-closed bit

    // Status byte (offset 5) bit map. (docs/research/continuity-parser.md
    // "Status byte (offset 5) — bit map".)
    private const int PrimaryInEarBit = 1 << 1;   // bit1: primary (current) pod in-ear
    private const int BothInCaseBit = 1 << 2;     // bit2: both pods in case
    private const int SecondaryInEarBit = 1 << 3; // bit3: secondary (other) pod in-ear
    private const int PrimaryIsLeftBit = 1 << 5;  // bit5: 1 → primary is left (the flip bit)
    private const int ThisPodInCaseBit = 1 << 6;  // bit6: broadcasting pod is in the case (in-ear XOR)

    // Charging bits live in the high nibble of the charging+case byte (offset 7).
    // (docs/research/continuity-parser.md "Charging bits (offset 7 high nibble)".)
    private const int PrimaryChargingBit = 1 << 4;   // byte bit4: primary pod charging
    private const int SecondaryChargingBit = 1 << 5; // byte bit5: secondary pod charging
    private const int CaseChargingBit = 1 << 6;      // byte bit6: case charging

    // Lid byte (offset 8) bit3 = lid closed (1 = closed → open = bit3 == 0).
    // (docs/research/continuity-parser.md "Lid byte (offset 8)".)
    private const int LidClosedBit = 1 << 3;

    // Battery nibble encoding: 0x0–0x9 → ×10 % (0–90); 0xA → 100 %; 0xF → unknown
    // sentinel; 0xB–0xE treated as unknown (conservative). (docs/research/
    // continuity-parser.md "Pods battery byte" + "Disputes".)
    private const int MaxBatteryNibble = 0x0A;

    /// <summary>
    /// Attempts to decode the first proximity-pairing (type <c>0x07</c>) entry in a
    /// company-id-stripped Apple manufacturer-data payload.
    /// </summary>
    /// <param name="manufacturerData">
    /// The manufacturer-data bytes with the <c>0x004C</c> company id already stripped
    /// (index 0 = first Continuity TLV type byte).
    /// </param>
    /// <param name="data">The decoded proximity data when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a valid proximity-pairing entry was found and decoded.</returns>
    public static bool TryParse(
        IReadOnlyList<byte> manufacturerData,
        [NotNullWhen(true)] out ContinuityProximityData? data)
    {
        data = null;
        if (!TryFindProximityBlock(manufacturerData, out var start))
        {
            return false;
        }

        data = Decode(manufacturerData, start);
        return true;
    }

    // Scan the TLV chain for a type-0x07, length-0x19 entry rather than assuming it
    // is first (a real advertisement can concatenate several Continuity TLVs).
    // (docs/research/continuity-parser.md "Robustness note".)
    private static bool TryFindProximityBlock(IReadOnlyList<byte> data, out int start)
    {
        start = 0;
        if (data is null)
        {
            return false;
        }

        var i = 0;
        while (i + 1 < data.Count) // need at least [type][length]
        {
            var length = data[i + 1];
            if (data[i] == ProximityType
                && length == ProximityValueLength
                && i + ProximityBlockLength <= data.Count)
            {
                start = i;
                return true;
            }

            i += 2 + length; // advance past this TLV's value to the next entry
        }

        return false;
    }

    private static ContinuityProximityData Decode(IReadOnlyList<byte> data, int p)
    {
        var status = data[p + StatusOffset];
        var battery = data[p + BatteryOffset];
        var chargingCase = data[p + ChargingCaseOffset];

        // Fields are ordered primary (broadcasting) pod then secondary; map to
        // physical left/right via status bit5. isFlipped ⇒ primary is the right pod.
        var primaryIsLeft = (status & PrimaryIsLeftBit) != 0;
        var isFlipped = !primaryIsLeft;
        var thisPodInCase = (status & ThisPodInCaseBit) != 0;

        var primaryNibble = battery & 0x0F;         // low nibble = primary pod
        var secondaryNibble = (battery >> 4) & 0x0F; // high nibble = secondary pod
        var primaryCharging = (chargingCase & PrimaryChargingBit) != 0;
        var secondaryCharging = (chargingCase & SecondaryChargingBit) != 0;

        // In-ear takes an extra XOR with bit6: xorFactor = primaryIsLeft ⊕ thisPodInCase;
        // isLeftInEar = xorFactor ? bit3 : bit1 (docs/research/continuity-parser.md
        // "Left/right vs primary/secondary flip").
        var primaryInEar = (status & PrimaryInEarBit) != 0;
        var secondaryInEar = (status & SecondaryInEarBit) != 0;
        var xorFactor = primaryIsLeft ^ thisPodInCase;

        return new ContinuityProximityData
        {
            Model = ToModel(data[p + ModelLowOffset], data[p + ModelHighOffset]),
            LeftBatteryPercent = DecodeBatteryNibble(isFlipped ? secondaryNibble : primaryNibble),
            RightBatteryPercent = DecodeBatteryNibble(isFlipped ? primaryNibble : secondaryNibble),
            CaseBatteryPercent = DecodeBatteryNibble(chargingCase & 0x0F), // offset 7 low nibble
            LeftCharging = isFlipped ? secondaryCharging : primaryCharging,
            RightCharging = isFlipped ? primaryCharging : secondaryCharging,
            CaseCharging = (chargingCase & CaseChargingBit) != 0,
            LeftInEar = xorFactor ? secondaryInEar : primaryInEar,
            RightInEar = xorFactor ? primaryInEar : secondaryInEar,
            LidOpen = DecodeLid(status, data[p + LidOffset]),
        };
    }

    private static int? DecodeBatteryNibble(int nibble)
        => nibble > MaxBatteryNibble ? null : nibble * 10; // 0xA→100; >0xA→unknown

    private static bool? DecodeLid(byte status, byte lid)
    {
        // The lid-closed bit is only trustworthy for in-case broadcasts (bit6 or
        // bit2); an out-of-case frame carries a stale lid byte → report unknown.
        var inCaseBroadcast = (status & ThisPodInCaseBit) != 0 || (status & BothInCaseBit) != 0;
        return inCaseBroadcast ? (lid & LidClosedBit) == 0 : null;
    }

    private static AirPodsModel ToModel(byte low, byte high)
    {
        // Little-endian model id: model = offset3 | offset4 << 8 → 0x20xx family.
        var raw = (ushort)(low | (high << 8));
        return Enum.IsDefined((AirPodsModel)raw) ? (AirPodsModel)raw : AirPodsModel.Unknown;
    }
}
