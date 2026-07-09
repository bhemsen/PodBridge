using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for the pure
/// <see cref="ConnectionStatusText"/> mapper that drives the tray status line:
/// every <see cref="ConnectionStatus"/> maps to a stable, non-empty phrase, and a
/// fake monitor's connect/disconnect transitions render "Connected"/"Disconnected".
/// </summary>
public class ConnectionStatusTextTests
{
    [Theory]
    [InlineData(ConnectionStatus.Connected, "Connected")]
    [InlineData(ConnectionStatus.Disconnected, "Disconnected")]
    [InlineData(ConnectionStatus.NoDevice, "No AirPods paired")]
    [InlineData(ConnectionStatus.BluetoothUnavailable, "Bluetooth unavailable")]
    [InlineData(ConnectionStatus.Unknown, "—")]
    public void ForStatus_MapsEachStatusToItsPhrase(ConnectionStatus status, string expected)
        => Assert.Equal(expected, ConnectionStatusText.ForStatus(status));

    [Fact]
    public void ForStatus_ReturnsNonEmptyForEveryDefinedStatus()
    {
        foreach (ConnectionStatus status in Enum.GetValues<ConnectionStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ConnectionStatusText.ForStatus(status)));
        }
    }

    [Fact]
    public void FakeMonitorConnectDisconnect_RendersConnectedThenDisconnected()
    {
        var monitor = new FakeConnectionMonitor();
        var rendered = new List<string>();
        monitor.StatusChanged += (_, status) => rendered.Add(ConnectionStatusText.ForStatus(status));

        monitor.SimulateConnected();
        monitor.SimulateDisconnected();

        Assert.Equal(2, rendered.Count);
        Assert.Equal("Connected", rendered[0]);
        Assert.Equal("Disconnected", rendered[1]);
    }

    [Fact]
    public void FakeMonitorNoDevice_RendersPairingGuidancePhrase()
    {
        var monitor = new FakeConnectionMonitor();

        monitor.SimulateNoDevice();

        Assert.Equal("No AirPods paired", ConnectionStatusText.ForStatus(monitor.CurrentStatus));
    }
}
