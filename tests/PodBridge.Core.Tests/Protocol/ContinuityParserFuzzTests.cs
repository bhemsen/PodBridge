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
