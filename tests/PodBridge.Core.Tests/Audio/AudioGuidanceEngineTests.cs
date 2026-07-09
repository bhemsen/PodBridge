using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for <see cref="AudioGuidanceEngine"/>,
/// driven through a fake <see cref="IAudioStateReader"/>. Asserts the honest display + advice
/// mapping for <b>every</b> enum state and that no guidance string makes a forbidden claim
/// (Apple-parity, paid driver, or a "force AAC" action).
/// </summary>
public class AudioGuidanceEngineTests
{
    // Phrases the constitution's "Honest audio surface" + Don'ts forbid in any guidance.
    private static readonly string[] ForbiddenSubstrings =
    [
        "apple",
        "force aac",
        "alternative a2dp driver",
        "fdk-aac",
        "fdk aac",
        "parity",
        "identical",
    ];

    [Fact]
    public void Sbc_ProducesAacAdviceState()
    {
        var guidance = Guide(CodecKind.Sbc, MicMode.HighQualityA2dp);

        Assert.Equal(AudioGuidanceEngine.CodecLineSbc, guidance.CodecLine);
        Assert.True(guidance.ShowAacAdvice);
        Assert.False(string.IsNullOrWhiteSpace(guidance.Advice));
        Assert.Contains("AAC", guidance.Advice, StringComparison.Ordinal);
        // Generic, driver-free advice per spec.
        Assert.Contains("Windows 11 21H2", guidance.Advice, StringComparison.Ordinal);
        Assert.Contains("driver", guidance.Advice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dongle", guidance.Advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aac_ProducesBestQualityStateWithNoAdvice()
    {
        var guidance = Guide(CodecKind.Aac, MicMode.HighQualityA2dp);

        Assert.Equal(AudioGuidanceEngine.CodecLineAac, guidance.CodecLine);
        Assert.Contains("AAC", guidance.CodecLine, StringComparison.Ordinal);
        Assert.Contains("best available", guidance.CodecLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(guidance.ShowAacAdvice);
        Assert.Null(guidance.Advice);
    }

    [Fact]
    public void UnknownCodec_ProducesCouldNotDetermineStateWithNoAdvice()
    {
        var guidance = Guide(CodecKind.Unknown, MicMode.HighQualityA2dp);

        Assert.Equal(AudioGuidanceEngine.CodecLineUnknown, guidance.CodecLine);
        Assert.Contains("couldn't determine", guidance.CodecLine, StringComparison.OrdinalIgnoreCase);
        Assert.False(guidance.ShowAacAdvice);
        Assert.Null(guidance.Advice);
    }

    [Fact]
    public void HighQualityA2dp_ProducesHighQualityMicLine()
    {
        var guidance = Guide(CodecKind.Aac, MicMode.HighQualityA2dp);

        Assert.Equal(AudioGuidanceEngine.MicLineHighQuality, guidance.MicModeLine);
        Assert.Contains("High quality", guidance.MicModeLine, StringComparison.Ordinal);
        Assert.Contains("A2DP", guidance.MicModeLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CallModeHfp_ProducesCallModeMonoMicLine()
    {
        var guidance = Guide(CodecKind.Aac, MicMode.CallModeHfp);

        Assert.Equal(AudioGuidanceEngine.MicLineCallMode, guidance.MicModeLine);
        Assert.Contains("Call mode", guidance.MicModeLine, StringComparison.Ordinal);
        Assert.Contains("mono", guidance.MicModeLine, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMic_ProducesCouldNotDetermineMicLine()
    {
        var guidance = Guide(CodecKind.Aac, MicMode.Unknown);

        Assert.Equal(AudioGuidanceEngine.MicLineUnknown, guidance.MicModeLine);
        Assert.Contains("couldn't determine", guidance.MicModeLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShowAacAdvice_IsTrueOnlyForSbc()
    {
        foreach (var codec in Enum.GetValues<CodecKind>())
        {
            var guidance = Guide(codec, MicMode.HighQualityA2dp);
            Assert.Equal(codec == CodecKind.Sbc, guidance.ShowAacAdvice);
        }
    }

    [Fact]
    public void EveryStateProducesNonEmptyCodecAndMicLines()
    {
        foreach (var codec in Enum.GetValues<CodecKind>())
        {
            foreach (var mic in Enum.GetValues<MicMode>())
            {
                var guidance = Guide(codec, mic);
                Assert.False(string.IsNullOrWhiteSpace(guidance.CodecLine));
                Assert.False(string.IsNullOrWhiteSpace(guidance.MicModeLine));
            }
        }
    }

    [Fact]
    public void NoGuidanceStringMakesAForbiddenClaim()
    {
        foreach (var codec in Enum.GetValues<CodecKind>())
        {
            foreach (var mic in Enum.GetValues<MicMode>())
            {
                var guidance = Guide(codec, mic);
                foreach (var text in new[] { guidance.CodecLine, guidance.MicModeLine, guidance.Advice })
                {
                    AssertNoForbiddenClaim(text);
                }
            }
        }
    }

    private static void AssertNoForbiddenClaim(string? text)
    {
        if (text is null)
        {
            return;
        }

        foreach (var forbidden in ForbiddenSubstrings)
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Drives the mapping through the read-only reader, exactly as the App will.
    private static AudioGuidance Guide(CodecKind codec, MicMode mic)
    {
        var reader = new FakeAudioStateReader();
        reader.Set(codec, mic);
        return AudioGuidanceEngine.ForState(reader.Read());
    }
}
