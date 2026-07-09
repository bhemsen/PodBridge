using PodBridge.Core.Models;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent connection-status contract tests (constitution Tier-1 gate):
/// a fake <c>IConnectionMonitor</c> drives connect/disconnect and the test asserts
/// the status mapping and change-event behaviour — no physical device required.
/// </summary>
public class ConnectionMonitorTests
{
    [Fact]
    public void Connect_MapsToConnectedStatus()
    {
        var monitor = new FakeConnectionMonitor();
        var observed = new List<ConnectionStatus>();
        monitor.StatusChanged += (_, status) => observed.Add(status);

        monitor.SimulateConnected();

        Assert.Equal(ConnectionStatus.Connected, monitor.CurrentStatus);
        Assert.Equal(new[] { ConnectionStatus.Connected }, observed);
    }

    [Fact]
    public void Disconnect_MapsToDisconnectedStatus()
    {
        var monitor = new FakeConnectionMonitor();
        monitor.SimulateConnected();
        var observed = new List<ConnectionStatus>();
        monitor.StatusChanged += (_, status) => observed.Add(status);

        monitor.SimulateDisconnected();

        Assert.Equal(ConnectionStatus.Disconnected, monitor.CurrentStatus);
        Assert.Equal(new[] { ConnectionStatus.Disconnected }, observed);
    }

    [Fact]
    public void ConnectThenDisconnect_RaisesBothTransitionsInOrder()
    {
        var monitor = new FakeConnectionMonitor();
        var observed = new List<ConnectionStatus>();
        monitor.StatusChanged += (_, status) => observed.Add(status);

        monitor.SimulateConnected();
        monitor.SimulateDisconnected();

        Assert.Equal(
            new[] { ConnectionStatus.Connected, ConnectionStatus.Disconnected },
            observed);
    }

    [Fact]
    public void RepeatedSameStatus_RaisesChangeOnlyOnce()
    {
        var monitor = new FakeConnectionMonitor();
        var raised = 0;
        monitor.StatusChanged += (_, _) => raised++;

        monitor.SimulateConnected();
        monitor.SimulateConnected();

        Assert.Equal(1, raised);
        Assert.Equal(ConnectionStatus.Connected, monitor.CurrentStatus);
    }

    [Fact]
    public void BluetoothUnavailable_IsSurfacedWithoutThrowing()
    {
        var monitor = new FakeConnectionMonitor();
        var observed = new List<ConnectionStatus>();
        monitor.StatusChanged += (_, status) => observed.Add(status);

        monitor.SimulateBluetoothUnavailable();

        Assert.Equal(ConnectionStatus.BluetoothUnavailable, monitor.CurrentStatus);
        Assert.Contains(ConnectionStatus.BluetoothUnavailable, observed);
    }

    [Fact]
    public async Task StartAsync_ThenStop_TogglesLifecycle()
    {
        var monitor = new FakeConnectionMonitor();

        await monitor.StartAsync();
        Assert.True(monitor.IsStarted);

        monitor.Stop();
        Assert.False(monitor.IsStarted);
    }
}
