using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Tier-1 tests for the tray's connect/disconnect presentation: when each action is
/// offered, that every failure falls back to Windows Bluetooth settings, and that
/// success messages stay honest.
/// </summary>
public class BluetoothAudioLinkTextTests
{
    [Theory]
    [InlineData(ConnectionStatus.Unknown, true)]
    [InlineData(ConnectionStatus.BluetoothUnavailable, true)]
    [InlineData(ConnectionStatus.NoDevice, true)]
    [InlineData(ConnectionStatus.Disconnected, true)]
    [InlineData(ConnectionStatus.Connected, false)]
    public void CanConnect_OfferedUnlessConnected(ConnectionStatus status, bool expected)
        => Assert.Equal(expected, BluetoothAudioLinkText.CanConnect(status));

    [Theory]
    [InlineData(ConnectionStatus.Unknown, true)]
    [InlineData(ConnectionStatus.BluetoothUnavailable, false)]
    [InlineData(ConnectionStatus.NoDevice, false)]
    [InlineData(ConnectionStatus.Disconnected, false)]
    [InlineData(ConnectionStatus.Connected, true)]
    public void CanDisconnect_OfferedWhenConnectedOrUnknown(ConnectionStatus status, bool expected)
        => Assert.Equal(expected, BluetoothAudioLinkText.CanDisconnect(status));

    [Theory]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.NotFound)]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.Unsupported)]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.Rejected)]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.TimedOut)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.NotFound)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.Unsupported)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.Rejected)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.TimedOut)]
    public void ForOutcome_Failure_FallsBackToBluetoothSettings(
        BluetoothAudioRequest request, BluetoothAudioLinkOutcome outcome)
    {
        var notice = BluetoothAudioLinkText.ForOutcome(request, outcome);

        Assert.NotNull(notice);
        Assert.True(notice.OpenBluetoothSettings);
        Assert.Contains("Bluetooth settings", notice.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.Succeeded)]
    [InlineData(BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.AlreadyInState)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.Succeeded)]
    [InlineData(BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.AlreadyInState)]
    public void ForOutcome_Success_DoesNotOpenSettings(
        BluetoothAudioRequest request, BluetoothAudioLinkOutcome outcome)
    {
        var notice = BluetoothAudioLinkText.ForOutcome(request, outcome);

        Assert.NotNull(notice);
        Assert.False(notice.OpenBluetoothSettings);
        Assert.False(string.IsNullOrWhiteSpace(notice.Title));
        Assert.False(string.IsNullOrWhiteSpace(notice.Message));
    }

    [Theory]
    [InlineData(BluetoothAudioRequest.Connect)]
    [InlineData(BluetoothAudioRequest.Disconnect)]
    public void ForOutcome_Busy_ShowsNothing(BluetoothAudioRequest request)
        => Assert.Null(BluetoothAudioLinkText.ForOutcome(request, BluetoothAudioLinkOutcome.Busy));

    [Fact]
    public void ForOutcome_DisconnectSucceeded_SaysTheyStayPairedAndHowToReconnect()
    {
        var notice = BluetoothAudioLinkText.ForOutcome(
            BluetoothAudioRequest.Disconnect, BluetoothAudioLinkOutcome.Succeeded);

        Assert.NotNull(notice);
        Assert.Contains("stay paired", notice.Message, StringComparison.Ordinal);
        Assert.Contains(BluetoothAudioLinkText.ConnectLabel, notice.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForOutcome_ConnectTimedOut_GivesAnActionableHint()
    {
        var notice = BluetoothAudioLinkText.ForOutcome(
            BluetoothAudioRequest.Connect, BluetoothAudioLinkOutcome.TimedOut);

        Assert.NotNull(notice);
        Assert.Contains("case", notice.Message, StringComparison.Ordinal);
    }
}
