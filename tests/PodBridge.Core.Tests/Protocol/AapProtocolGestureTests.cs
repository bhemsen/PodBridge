using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

/// <summary>
/// Locks the clean-room AAP press-and-hold gesture-remap byte format to the researched
/// values (docs/research/gesture-aap.md, issue #46). Device-independent — the frame is
/// built by <see cref="AapProtocol"/> directly; one test also drives it through a fake
/// <see cref="IAapTransport"/> to prove the exact bytes reach the wire with left/right
/// addressing intact, no physical AirPods required.
/// </summary>
public class AapProtocolGestureTests
{
    // The setting identifier for the press-and-hold gesture is 0x16 (ClickHoldMode);
    // the two data bytes are right-then-left. (docs/research/gesture-aap.md.)
    [Theory]
    [InlineData(GestureAction.NoiseControl, GestureAction.Siri, 0x01, 0x05)]
    [InlineData(GestureAction.Siri, GestureAction.NoiseControl, 0x05, 0x01)]
    [InlineData(GestureAction.NoiseControl, GestureAction.NoiseControl, 0x01, 0x01)]
    [InlineData(GestureAction.Siri, GestureAction.Siri, 0x05, 0x05)]
    public void BuildSetPressAndHoldGesture_EncodesRightThenLeftAtBytes7And8(
        GestureAction rightBud, GestureAction leftBud, byte rightByte, byte leftByte)
    {
        byte[] expected =
            [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x16, rightByte, leftByte, 0x00, 0x00];
        Assert.Equal(expected, AapProtocol.BuildSetPressAndHoldGesture(rightBud, leftBud));
    }

    [Fact]
    public void BuildSetPressAndHoldGesture_AddressesRightAndLeftDistinctly()
    {
        // Asymmetric assignment must NOT be symmetric on the wire: right=data1 (byte 7),
        // left=data2 (byte 8). (docs/research/gesture-aap.md "byte order explicit".)
        var frame = AapProtocol.BuildSetPressAndHoldGesture(
            rightBud: GestureAction.NoiseControl, leftBud: GestureAction.Siri);
        Assert.Equal(0x01, frame[7]); // right bud
        Assert.Equal(0x05, frame[8]); // left bud
    }

    [Fact]
    public void BuildSetPressAndHoldGesture_ConfigurationOverload_MatchesActionOverload()
    {
        var config = new GestureConfiguration(
            RightBud: GestureAction.Siri, LeftBud: GestureAction.NoiseControl);
        Assert.Equal(
            AapProtocol.BuildSetPressAndHoldGesture(GestureAction.Siri, GestureAction.NoiseControl),
            AapProtocol.BuildSetPressAndHoldGesture(config));
    }

    [Fact]
    public void BuildSetPressAndHoldGesture_ConfigurationOverload_RejectsNull()
        => Assert.Throws<ArgumentNullException>(
            () => AapProtocol.BuildSetPressAndHoldGesture((GestureConfiguration)null!));

    [Fact]
    public void BuildSetPressAndHoldGesture_RejectsUndefinedRightAction()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AapProtocol.BuildSetPressAndHoldGesture((GestureAction)0x02, GestureAction.Siri));

    [Fact]
    public void BuildSetPressAndHoldGesture_RejectsUndefinedLeftAction()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AapProtocol.BuildSetPressAndHoldGesture(GestureAction.NoiseControl, (GestureAction)0x63));

    [Theory]
    [InlineData(GestureAction.NoiseControl, GestureAction.Siri)]
    [InlineData(GestureAction.Siri, GestureAction.NoiseControl)]
    [InlineData(GestureAction.NoiseControl, GestureAction.NoiseControl)]
    public void SetThenParse_RoundTripsPerBudActions(GestureAction rightBud, GestureAction leftBud)
    {
        // The echo/notification uses the identical layout as the SET frame, so a built
        // SET frame must parse back to the same per-bud config (write+echo confirm).
        var frame = AapProtocol.BuildSetPressAndHoldGesture(rightBud, leftBud);
        Assert.True(AapProtocol.TryParsePressAndHoldGestureNotification(frame, out var parsed));
        Assert.Equal(new GestureConfiguration(rightBud, leftBud), parsed);
    }

    [Fact]
    public void TryParse_RejectsWrongLength()
    {
        byte[] shortFrame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x16, 0x01];
        Assert.False(AapProtocol.TryParsePressAndHoldGestureNotification(shortFrame, out var config));
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_RejectsWrongHeader()
    {
        byte[] frame = [0x04, 0x00, 0x04, 0x01, 0x09, 0x00, 0x16, 0x01, 0x05, 0x00, 0x00];
        Assert.False(AapProtocol.TryParsePressAndHoldGestureNotification(frame, out _));
    }

    [Theory]
    [InlineData(0x0D)] // ListeningMode (noise control), not a gesture
    [InlineData(0x14)] // SingleClickMode — fixed by Apple, must not be treated as a gesture set
    [InlineData(0x15)] // DoubleClickMode — fixed by Apple, must not be treated as a gesture set
    public void TryParse_RejectsNonClickHoldIdentifier(byte identifier)
    {
        byte[] frame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, identifier, 0x01, 0x05, 0x00, 0x00];
        Assert.False(AapProtocol.TryParsePressAndHoldGestureNotification(frame, out _));
    }

    [Theory]
    [InlineData(0x00)] // no action
    [InlineData(0x02)] // undocumented action byte
    [InlineData(0x06)] // undocumented action byte
    public void TryParse_RejectsUnknownActionByte(byte actionByte)
    {
        byte[] frame = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x16, actionByte, 0x05, 0x00, 0x00];
        Assert.False(AapProtocol.TryParsePressAndHoldGestureNotification(frame, out _));
    }

    [Fact]
    public async Task BuildSetPressAndHoldGesture_WrittenOverFakeTransport_SendsExactBytes()
    {
        // Acceptance: a fake IAapTransport records that the gesture assignment reaches the
        // wire as the exact expected bytes with right/left addressing intact.
        var transport = new FakeAapTransport();
        var frame = AapProtocol.BuildSetPressAndHoldGesture(
            rightBud: GestureAction.NoiseControl, leftBud: GestureAction.Siri);

        await transport.SendAsync(frame);

        byte[] expected = [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, 0x16, 0x01, 0x05, 0x00, 0x00];
        Assert.Equal(expected, Assert.Single(transport.Sent));
    }
}
