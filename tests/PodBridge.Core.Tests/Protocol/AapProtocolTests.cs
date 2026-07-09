using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Locks the clean-room AAP noise-control byte format to the researched values
/// (docs/research/aap-anc-protocol.md). Device-independent — no transport needed.
/// </summary>
public class AapProtocolTests
{
    [Fact]
    public void BuildHandshake_MatchesResearchedBytes()
    {
        byte[] expected =
            [0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(expected, AapProtocol.BuildHandshake());
    }

    [Fact]
    public void BuildSetSpecificFeatures_MatchesResearchedBytes()
    {
        byte[] expected = [0x04, 0x00, 0x04, 0x00, 0x4D, 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(expected, AapProtocol.BuildSetSpecificFeatures());
    }

    [Fact]
    public void BuildRequestNotifications_MatchesResearchedBytes()
    {
        byte[] expected = [0x04, 0x00, 0x04, 0x00, 0x0F, 0x00, 0xFF, 0xFF, 0xFE, 0xFF];
        Assert.Equal(expected, AapProtocol.BuildRequestNotifications());
    }

    [Theory]
    [InlineData(NoiseControlMode.Off, 0x01)]
    [InlineData(NoiseControlMode.NoiseCancellation, 0x02)]
    [InlineData(NoiseControlMode.Transparency, 0x03)]
    [InlineData(NoiseControlMode.Adaptive, 0x04)]
    public void BuildSetNoiseControl_EncodesModeAtByte7(NoiseControlMode mode, byte modeByte)
    {
        byte[] expected = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, modeByte, 0x00, 0x00, 0x00];
        Assert.Equal(expected, AapProtocol.BuildSetNoiseControl(mode));
    }

    [Fact]
    public void BuildSetNoiseControl_RejectsUndefinedMode()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AapProtocol.BuildSetNoiseControl((NoiseControlMode)0x63));

    [Theory]
    [InlineData(NoiseControlMode.Off)]
    [InlineData(NoiseControlMode.NoiseCancellation)]
    [InlineData(NoiseControlMode.Transparency)]
    [InlineData(NoiseControlMode.Adaptive)]
    public void SetThenParse_RoundTripsEachMode(NoiseControlMode mode)
    {
        // The echo/notification uses the identical layout as the SET frame, so a built
        // SET frame must parse back to the requested mode (docs/research: echo confirm).
        var frame = AapProtocol.BuildSetNoiseControl(mode);
        Assert.True(AapProtocol.TryParseNoiseControlNotification(frame, out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Fact]
    public void TryParse_RejectsWrongLength()
    {
        byte[] shortFrame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, 0x02];
        Assert.False(AapProtocol.TryParseNoiseControlNotification(shortFrame, out var mode));
        Assert.Null(mode);
    }

    [Fact]
    public void TryParse_RejectsWrongOpcodeOrHeader()
    {
        // Right length, wrong setting id (0x2E = adaptive-strength, not ListeningMode).
        byte[] frame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x2E, 0x02, 0x00, 0x00, 0x00];
        Assert.False(AapProtocol.TryParseNoiseControlNotification(frame, out _));
    }

    [Fact]
    public void TryParse_RejectsUnknownModeByte()
    {
        byte[] frame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x0D, 0x05, 0x00, 0x00, 0x00];
        Assert.False(AapProtocol.TryParseNoiseControlNotification(frame, out _));
    }
}
