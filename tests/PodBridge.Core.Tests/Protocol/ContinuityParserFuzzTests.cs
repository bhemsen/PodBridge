using System.Linq;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent fuzz/property tests (constitution Tier-1 hardening gate; spec
/// docs/specs/spec-model-coverage-hardening.md) proving the clean-room Continuity parser
/// and the model registry tolerate malformed input: truncated, over-long, and random
/// payloads <b>never throw</b> and <b>never mis-identify a known model's fixture</b>, and
/// every decoded battery value stays within a sane range.
/// </summary>
public class ContinuityParserFuzzTests
{
    // Every AirPodsModel the parser can name (all defined enum values except the
    // catch-all Unknown) — the "known model fixtures" the fuzzing must never corrupt.
    private static IEnumerable<AirPodsModel> KnownModels()
        => Enum.GetValues<AirPodsModel>().Where(m => m != AirPodsModel.Unknown);

    [Fact]
    public void RandomPayloads_OfAnyLength_NeverThrow_AndDecodeSaneBattery()
    {
        var rng = new Random(0xC0FFEE);
        for (var i = 0; i < 20_000; i++)
        {
            var payload = new byte[rng.Next(0, 300)];
            rng.NextBytes(payload);

            // The property under test: this call must never throw for ANY input.
            if (ContinuityParser.TryParse(payload, out var data))
            {
                AssertSaneBattery(data);
            }
        }
    }

    [Fact]
    public void EveryByteValueAtEachDecodedOffset_NeverThrows_AndStaysSane()
    {
        for (var value = 0; value <= 0xFF; value++)
        {
            var b = (byte)value;
            // Vary each decoded byte across its full range while holding the others,
            // exercising every status/battery/charging/lid nibble+bit combination.
            AssertParsesSane(ContinuityFixtures.Proximity(status: b, battery: 0x55, chargingCase: 0x05));
            AssertParsesSane(ContinuityFixtures.Proximity(status: 0x20, battery: b, chargingCase: 0x05));
            AssertParsesSane(ContinuityFixtures.Proximity(status: 0x20, battery: 0x55, chargingCase: b));
            AssertParsesSane(ContinuityFixtures.Proximity(status: 0x40, battery: 0x55, chargingCase: 0x05, lid: b));
        }
    }

    [Fact]
    public void TruncatedKnownFixtures_NeverThrow_AndAreRejected()
    {
        foreach (var model in KnownModels())
        {
            var full = ContinuityFixtures.Proximity(status: 0x20, battery: 0x55, chargingCase: 0x05, model: (ushort)model);
            for (var len = 0; len < full.Length; len++)
            {
                var truncated = full[..len];
                // A truncated block can never satisfy the length/bounds check, so it is
                // rejected — never a throw, and never a confident (mis-)identification.
                Assert.False(ContinuityParser.TryParse(truncated, out _));
            }
        }
    }

    [Fact]
    public void OverLongKnownFixtures_NeverThrow_AndStillIdentifyTheCorrectModel()
    {
        var rng = new Random(0xBADF00D);
        foreach (var model in KnownModels())
        {
            var full = ContinuityFixtures.Proximity(status: 0x20, battery: 0x55, chargingCase: 0x05, model: (ushort)model);
            for (var extra = 1; extra <= 200; extra++)
            {
                var trailing = new byte[extra];
                rng.NextBytes(trailing);
                var overlong = new byte[full.Length + extra];
                full.CopyTo(overlong, 0);
                trailing.CopyTo(overlong, full.Length);

                Assert.True(ContinuityParser.TryParse(overlong, out var data));
                Assert.Equal(model, data.Model); // trailing garbage never corrupts the model
                AssertSaneBattery(data);
            }
        }
    }

    // THREAT-MODEL.md "Surface A": CVE-2023-24871 was an out-of-bounds write in
    // Windows' own BLE advertisement parser driven by an attacker-controlled
    // length/count field. These tests assert the analogous field in this
    // codebase — the TLV `length` byte, which doubles as the chain-advance
    // "count" of bytes to skip in `TryFindProximityBlock` — can never cause an
    // out-of-bounds read or a throw, at explicit truncated, over-long, and
    // boundary values rather than relying only on randomized fuzzing to hit them.
    [Theory]
    [InlineData((byte)0x00)] // minimum possible length
    [InlineData((byte)0x01)]
    [InlineData((byte)0x18)] // one less than the valid proximity length (0x19)
    [InlineData((byte)0x19)] // the valid proximity length itself
    [InlineData((byte)0x1A)] // one more than the valid proximity length
    [InlineData((byte)0x7F)]
    [InlineData((byte)0xFE)]
    [InlineData((byte)0xFF)] // maximum possible length
    public void MalformedLengthField_HeaderOnlyNoValueBytesPresent_NeverThrows_AndIsRejected(byte length)
    {
        // Only the [type][length] header is present; zero of the declared
        // value bytes actually follow. A parser that trusted `length` to index
        // before checking the buffer size would read out of bounds here.
        var payload = new byte[] { 0x07, length };
        Assert.False(ContinuityParser.TryParse(payload, out _));
    }

    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x18)]
    [InlineData((byte)0x19)] // the correct value length, but too few bytes actually follow
    [InlineData((byte)0x1A)]
    [InlineData((byte)0x7F)]
    [InlineData((byte)0xFF)]
    public void MalformedLengthField_DeclaredLengthExceedsAvailableBytes_NeverThrows_AndIsRejected(byte length)
    {
        // type 0x07 with a declared length that (for most values here) claims
        // far more value bytes than the seven that actually follow — directly
        // exercising the `i + ProximityBlockLength <= data.Count` bounds check
        // at the boundary rather than hoping random fuzzing lands on it.
        var payload = new byte[] { 0x07, length, 0x01, 0x02, 0x03, 0x04, 0x05 };
        Assert.False(ContinuityParser.TryParse(payload, out _));
    }

    [Fact]
    public void MalformedLengthField_CausesChainAdvancePastBufferEnd_NeverThrows_ForEveryPossibleValue()
    {
        // A non-proximity TLV (type 0x00) with every possible length/count byte
        // 0x00-0xFF, followed by only a handful of trailing bytes. Many of
        // these lengths make the chain-advance (`i += 2 + length`) walk the
        // scan index past `data.Count`; the loop must terminate via its own
        // `i + 1 < data.Count` bounds check, never by dereferencing past the end.
        for (var length = 0; length <= 0xFF; length++)
        {
            var payload = new byte[] { 0x00, (byte)length, 0xAA, 0xBB, 0xCC };
            Assert.False(ContinuityParser.TryParse(payload, out _));
        }
    }

    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x05)]
    [InlineData((byte)0x19)]
    [InlineData((byte)0xFF)]
    public void MalformedLeadingTlv_WithArbitraryLengthBeforeAGenuineBlock_NeverThrows(byte leadingLength)
    {
        // A bogus leading TLV with an attacker-controlled length/count byte,
        // followed by an otherwise-genuine, well-formed proximity block. Only
        // memory safety is asserted: whether the genuine block is still found
        // depends on where the chain-advance lands, which is not the point
        // under test here — never throwing and never a malformed result is.
        var leading = new byte[] { 0x00, leadingLength };
        var genuine = ContinuityFixtures.Proximity(status: 0x20, battery: 0x55, chargingCase: 0x05);
        var payload = leading.Concat(genuine).ToArray();

        if (ContinuityParser.TryParse(payload, out var data))
        {
            AssertSaneBattery(data);
        }
    }

    [Fact]
    public void ModelRegistry_ResolvesEveryPossibleModelValue_NeverThrows()
    {
        var registry = new ModelRegistry();
        for (var raw = 0; raw <= 0xFFFF; raw++)
        {
            var info = registry.Resolve((AirPodsModel)raw);
            Assert.NotNull(info);
            Assert.False(string.IsNullOrEmpty(info.DisplayName)); // always labelled, never blank
        }
    }

    [Fact]
    public void ModelRegistry_UnrecognisedValues_DegradeToLabelledUnknown_NeverThrow()
    {
        var registry = new ModelRegistry();
        var known = new HashSet<ushort>(KnownModels().Select(m => (ushort)m));
        var rng = new Random(0x5EED);
        for (var i = 0; i < 5_000; i++)
        {
            var raw = (ushort)rng.Next(0, 0x1_0000);
            var info = registry.Resolve((AirPodsModel)raw);
            if (!known.Contains(raw))
            {
                // Anything the six-model vision does not cover falls back to the generic,
                // never-crashing "Unknown AirPods" shape (never a false capability claim).
                Assert.False(info.IsRecognized);
                Assert.Equal("Unknown AirPods", info.DisplayName);
            }
        }
    }

    private static void AssertParsesSane(byte[] payload)
    {
        Assert.True(ContinuityParser.TryParse(payload, out var data));
        AssertSaneBattery(data);
    }

    private static void AssertSaneBattery(ContinuityProximityData data)
    {
        AssertPercent(data.LeftBatteryPercent);
        AssertPercent(data.RightBatteryPercent);
        AssertPercent(data.CaseBatteryPercent);
    }

    private static void AssertPercent(int? percent)
        => Assert.True(percent is null or (>= 0 and <= 100), $"battery percent out of range: {percent}");
}
