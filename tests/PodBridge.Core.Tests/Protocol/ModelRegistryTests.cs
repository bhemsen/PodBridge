using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Device-independent tests for <see cref="ModelRegistry"/>: fixture Continuity
/// payloads for each of the vision's six target models identify to the correct
/// per-model shape, connector variants fold into the same shape, and any other
/// identifier — including Phase-2 enum values outside the six-model vision scope —
/// degrades to the labelled "Unknown AirPods" generic fallback, never throwing
/// (docs/research/model-ids.md; spec docs/specs/spec-model-coverage-hardening.md).
/// </summary>
public class ModelRegistryTests
{
    private readonly ModelRegistry _registry = new();

    // Model ids per docs/research/continuity-parser.md / docs/research/model-ids.md,
    // matching the constants already used by ContinuityParserTests.
    [Theory]
    [InlineData(0x200F, "AirPods 2", true, true, true)]
    [InlineData(0x2013, "AirPods 3", true, true, true)]
    [InlineData(0x200E, "AirPods Pro", true, true, true)]
    [InlineData(0x2014, "AirPods Pro 2", true, true, true)]
    [InlineData(0x2024, "AirPods Pro 2", true, true, true)] // USB-C case: same shape
    [InlineData(0x2027, "AirPods Pro 3", true, true, true)]
    [InlineData(0x200A, "AirPods Max", false, false, false)]
    [InlineData(0x201F, "AirPods Max", false, false, false)] // USB-C case: same shape
    public void SixVisionModels_FixturePayload_IdentifiesWithCorrectShape(
        ushort modelId, string displayName, bool hasDualBuds, bool hasBatteryCase, bool hasInEarDetection)
    {
        var payload = ContinuityFixtures.Proximity(status: 0x20, battery: 0x00, chargingCase: 0x00, model: modelId);
        Assert.True(ContinuityParser.TryParse(payload, out var data));

        var info = _registry.Resolve(data.Model);

        Assert.True(info.IsRecognized);
        Assert.Equal(displayName, info.DisplayName);
        Assert.Equal(hasDualBuds, info.HasDualBuds);
        Assert.Equal(hasBatteryCase, info.HasBatteryCase);
        Assert.Equal(hasInEarDetection, info.HasInEarDetection);
    }

    [Fact]
    public void UnrecognisedModelId_FixturePayload_ResolvesToGenericFallback()
    {
        var payload = ContinuityFixtures.Proximity(status: 0x20, battery: 0x00, chargingCase: 0x00, model: 0x2099);
        Assert.True(ContinuityParser.TryParse(payload, out var data));
        Assert.Equal(AirPodsModel.Unknown, data.Model);

        var info = _registry.Resolve(data.Model);

        Assert.False(info.IsRecognized);
        Assert.Equal("Unknown AirPods", info.DisplayName);
        Assert.True(info.HasDualBuds);       // best-effort dual-bud assumption
        Assert.True(info.HasBatteryCase);    // best-effort case assumption
        Assert.True(info.HasInEarDetection); // best-effort in-ear assumption
    }

    // These are recognised by the Phase-2 parser (distinct AirPodsModel values) but
    // sit outside the vision's six supported models; the registry treats them the
    // same as a truly unrecognised id — never a crash, never a false capability claim.
    [Theory]
    [InlineData(AirPodsModel.AirPods1)]
    [InlineData(AirPodsModel.AirPods4)]
    [InlineData(AirPodsModel.AirPods4Anc)]
    [InlineData(AirPodsModel.BeatsFitPro)]
    public void OutOfVisionScopeModels_AlsoDegradeToGenericFallback(AirPodsModel model)
    {
        var info = _registry.Resolve(model);

        Assert.False(info.IsRecognized);
        Assert.Equal("Unknown AirPods", info.DisplayName);
    }

    [Fact]
    public void Resolve_NeverThrows_ForEveryKnownEnumValue()
    {
        foreach (var model in Enum.GetValues<AirPodsModel>())
        {
            Assert.NotNull(_registry.Resolve(model));
        }
    }
}
