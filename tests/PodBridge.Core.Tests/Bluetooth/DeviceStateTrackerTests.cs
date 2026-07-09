using Microsoft.Extensions.Time.Testing;
using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent tests for the connection-gated <see cref="DeviceStateTracker"/>
/// pipeline, driven by a fake scanner + fake connection monitor + a fake clock
/// (constitution Tier-1 test gate — no physical AirPods).
/// </summary>
public class DeviceStateTrackerTests
{
    // Not-flipped frame: left 70%, right 50%, case 90% + charging, left in-ear; AirPods Pro.
    private static readonly byte[] FrameA =
        ContinuityFixtures.Proximity(status: 0x28, battery: 0x57, chargingCase: 0x49, model: 0x200E);

    [Fact]
    public void ConnectedAndValidFrame_PublishesLiveDecodedState()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        DeviceState? published = null;
        tracker.StateChanged += (_, s) => published = s;

        monitor.SimulateConnected();
        scanner.Emit(AppleAdv(FrameA));

        var state = tracker.Current;
        Assert.True(state.IsLive);
        Assert.Equal(70, state.LeftBatteryPercent);
        Assert.Equal(50, state.RightBatteryPercent);
        Assert.Equal(90, state.CaseBatteryPercent);
        Assert.True(state.CaseCharging);
        Assert.True(state.LeftInEar);
        Assert.False(state.RightInEar);
        Assert.Equal(AirPodsModel.AirPodsPro, state.Model);
        Assert.Equal(state, published);
    }

    [Fact]
    public void Disconnected_GatesOutAdvertisement_NoLiveBattery()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        monitor.SimulateDisconnected();

        scanner.Emit(AppleAdv(FrameA));

        Assert.False(tracker.Current.IsLive);
        Assert.Null(tracker.Current.LeftBatteryPercent);
        Assert.Equal(DeviceState.Unknown, tracker.Current);
    }

    [Fact]
    public void NeverConnected_GatesOutAdvertisement()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor(); // stays Unknown
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());

        scanner.Emit(AppleAdv(FrameA));

        Assert.Equal(DeviceState.Unknown, tracker.Current);
    }

    [Fact]
    public void NonAppleAdvertisement_Ignored()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        monitor.SimulateConnected();

        // Same payload bytes but a non-Apple company id → must be ignored.
        scanner.Emit(new BleAdvertisement(0x2, -40, 0x0006, FrameA));

        Assert.False(tracker.Current.IsLive);
        Assert.Equal(DeviceState.Unknown, tracker.Current);
    }

    [Fact]
    public void AppleNonProximityFrame_Ignored()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        monitor.SimulateConnected();

        byte[] notProximity = new byte[27];
        notProximity[0] = 0x10; // nearby-info, not proximity pairing
        notProximity[1] = 0x19;
        scanner.Emit(AppleAdv(notProximity));

        Assert.Equal(DeviceState.Unknown, tracker.Current);
    }

    [Fact]
    public void LiveState_GoesStaleAfterTimeout()
    {
        var time = new FakeTimeProvider();
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, time);
        var changes = new List<DeviceState>();
        tracker.StateChanged += (_, s) => changes.Add(s);

        monitor.SimulateConnected();
        scanner.Emit(AppleAdv(FrameA));
        Assert.True(tracker.Current.IsLive);

        time.Advance(TimeSpan.FromSeconds(31));

        Assert.False(tracker.Current.IsLive);
        Assert.Equal(DeviceState.Unknown, tracker.Current);
        Assert.Equal(DeviceState.Unknown, changes[^1]);
    }

    [Fact]
    public void FreshAdvertisement_ResetsStalenessTimer()
    {
        var time = new FakeTimeProvider();
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, time);
        monitor.SimulateConnected();

        scanner.Emit(AppleAdv(FrameA));
        time.Advance(TimeSpan.FromSeconds(20));
        scanner.Emit(AppleAdv(FrameA));        // refresh before the 30 s timeout
        time.Advance(TimeSpan.FromSeconds(20)); // 40 s total, but only 20 s since refresh

        Assert.True(tracker.Current.IsLive);
    }

    [Fact]
    public void StrongerAdvertisementDisplaces_WeakerDifferentDeviceIgnored()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        monitor.SimulateConnected();

        scanner.Emit(AppleAdv(FrameA, rssi: -70, address: 0x1));
        Assert.Equal(70, tracker.Current.LeftBatteryPercent);

        // Weaker, different address → ignored (keeps the tracked device).
        var weak = ContinuityFixtures.Proximity(status: 0x20, battery: 0x11, chargingCase: 0x00);
        scanner.Emit(AppleAdv(weak, rssi: -80, address: 0x2));
        Assert.Equal(70, tracker.Current.LeftBatteryPercent);

        // Stronger, different address → displaces (left 30%).
        var strong = ContinuityFixtures.Proximity(status: 0x20, battery: 0x33, chargingCase: 0x00);
        scanner.Emit(AppleAdv(strong, rssi: -40, address: 0x3));
        Assert.Equal(30, tracker.Current.LeftBatteryPercent);
    }

    [Fact]
    public void DisconnectAfterLive_ReturnsToUnknown()
    {
        var scanner = new FakeBleScanner();
        var monitor = new FakeConnectionMonitor();
        using var tracker = new DeviceStateTracker(scanner, monitor, new FakeTimeProvider());
        var changes = new List<DeviceState>();
        tracker.StateChanged += (_, s) => changes.Add(s);

        monitor.SimulateConnected();
        scanner.Emit(AppleAdv(FrameA));
        Assert.True(tracker.Current.IsLive);

        monitor.SimulateDisconnected();

        Assert.False(tracker.Current.IsLive);
        Assert.Equal(DeviceState.Unknown, tracker.Current);
        Assert.Equal(DeviceState.Unknown, changes[^1]);
    }

    private static BleAdvertisement AppleAdv(byte[] payload, short rssi = -50, ulong address = 0x1)
        => new(address, rssi, AppleContinuity.AppleCompanyId, payload);
}
