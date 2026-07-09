using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent decode tests for the clean-room Continuity parser. Fixtures
/// are synthetic company-id-stripped proximity-pairing payloads; expected values
/// are derived by hand from docs/research/continuity-parser.md — including a
/// non-flipped (primary = left) and a flipped (primary = right) frame.
/// </summary>
public class ContinuityParserTests
{
    // Frame A — NOT flipped: status bit5=1 (primary=left), bit3=1 (secondary in-ear);
    // battery 0x57 → left(low)=70%, right(high)=50%; charging+case 0x49 → case 90%,
    // case charging (byte bit6); model 0x200E = AirPods Pro.
    [Fact]
    public void NonFlippedFrame_DecodesLeftRightCaseAndInEar()
    {
        var payload = Proximity(status: 0x28, battery: 0x57, chargingCase: 0x49, model: 0x200E);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(AirPodsModel.AirPodsPro, data.Model);
        Assert.Equal(70, data.LeftBatteryPercent);
        Assert.Equal(50, data.RightBatteryPercent);
        Assert.Equal(90, data.CaseBatteryPercent);
        Assert.False(data.LeftCharging);
        Assert.False(data.RightCharging);
        Assert.True(data.CaseCharging);
        Assert.True(data.LeftInEar);   // xorFactor=1 ⇒ left = bit3 (set)
        Assert.False(data.RightInEar); // xorFactor=1 ⇒ right = bit1 (clear)
    }

    // Frame B — FLIPPED: status bit5=0 (primary=right), bit1=1 (primary in-ear);
    // battery 0x39 → left(high)=30%, right(low)=90%; charging+case 0x1A → case 100%,
    // right(=primary) charging (byte bit4); model 0x2014 = AirPods Pro 2.
    [Fact]
    public void FlippedFrame_MapsPrimaryToRightAndSecondaryToLeft()
    {
        var payload = Proximity(status: 0x02, battery: 0x39, chargingCase: 0x1A, model: 0x2014);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(AirPodsModel.AirPodsPro2, data.Model);
        Assert.Equal(30, data.LeftBatteryPercent);
        Assert.Equal(90, data.RightBatteryPercent);
        Assert.Equal(100, data.CaseBatteryPercent); // low nibble 0xA → 100%
        Assert.False(data.LeftCharging);
        Assert.True(data.RightCharging);
        Assert.False(data.CaseCharging);
        Assert.True(data.LeftInEar);   // xorFactor=0 ⇒ left = bit1 (set)
        Assert.False(data.RightInEar); // xorFactor=0 ⇒ right = bit3 (clear)
    }

    // 0xF nibble is the unknown/absent sentinel → null, not a fake 0%.
    [Fact]
    public void UnknownNibbleSentinel_YieldsNullBattery()
    {
        var payload = Proximity(status: 0x20, battery: 0xF7, chargingCase: 0x0F);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(70, data.LeftBatteryPercent); // low nibble 7 (primary=left)
        Assert.Null(data.RightBatteryPercent);      // high nibble 0xF → unknown
        Assert.Null(data.CaseBatteryPercent);       // low nibble 0xF → unknown
    }

    [Theory]
    [InlineData(0xB0)] // 0xB high nibble → out of range → unknown
    [InlineData(0xE0)] // 0xE high nibble → out of range → unknown
    public void OutOfRangeNibble_TreatedAsUnknown(byte battery)
    {
        var payload = Proximity(status: 0x20, battery: battery, chargingCase: 0x00);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Null(data.RightBatteryPercent);
    }

    // Lid-closed bit is trustworthy only for in-case broadcasts (status bit6/bit2).
    [Theory]
    [InlineData(0x40, 0x00, true)]   // in-case broadcast, bit3 clear → lid open
    [InlineData(0x40, 0x08, false)]  // in-case broadcast, bit3 set → lid closed
    public void LidState_ReadWhenInCaseBroadcast(byte status, byte lid, bool expectedOpen)
    {
        var payload = Proximity(status: status, battery: 0x00, chargingCase: 0x00, lid: lid);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(expectedOpen, data.LidOpen);
    }

    [Fact]
    public void LidState_UnknownWhenNotInCaseBroadcast()
    {
        var payload = Proximity(status: 0x20, battery: 0x00, chargingCase: 0x00, lid: 0x08);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Null(data.LidOpen);
    }

    [Fact]
    public void UnknownModelId_MapsToUnknown()
    {
        var payload = Proximity(status: 0x20, battery: 0x55, chargingCase: 0x05, model: 0x2099);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(AirPodsModel.Unknown, data.Model);
    }

    // A real advertisement can concatenate several Continuity TLVs; the parser must
    // scan the chain for the 0x07 entry rather than assume it is first.
    [Fact]
    public void ProximityEntry_FoundAfterAnotherTlv()
    {
        var proximity = Proximity(status: 0x28, battery: 0x57, chargingCase: 0x49);
        byte[] prefixTlv = [0x10, 0x03, 0xAA, 0xBB, 0xCC]; // some other Continuity TLV
        var payload = new byte[prefixTlv.Length + proximity.Length];
        prefixTlv.CopyTo(payload, 0);
        proximity.CopyTo(payload, prefixTlv.Length);

        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(70, data.LeftBatteryPercent);
    }

    [Fact]
    public void WrongMessageType_IsRejected()
    {
        byte[] notProximity = new byte[27];
        notProximity[0] = 0x10; // nearby-info, not proximity-pairing
        notProximity[1] = 0x19;

        Assert.False(ContinuityParser.TryParse(notProximity, out var data));
        Assert.Null(data);
    }

    [Fact]
    public void TruncatedPayload_IsRejected()
    {
        byte[] truncated = [0x07, 0x19, 0x01, 0x0E, 0x20]; // claims length 25 but too short

        Assert.False(ContinuityParser.TryParse(truncated, out _));
    }

    [Fact]
    public void EmptyPayload_IsRejected()
        => Assert.False(ContinuityParser.TryParse([], out _));

    private static byte[] Proximity(
        byte status,
        byte battery,
        byte chargingCase,
        byte lid = 0x00,
        ushort model = 0x200E)
        => ContinuityFixtures.Proximity(status, battery, chargingCase, lid, model);
}
