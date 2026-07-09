using PodBridge.Core.Models;
using Xunit;

namespace PodBridge.Core.Tests.Models;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for the pure
/// <see cref="BatteryStatusText"/> mapper that drives the tray battery line: a live
/// snapshot renders left/right/case % with a charging indicator, the unknown-battery
/// sentinel renders per-component "unknown", and any not-live snapshot renders the
/// single "unknown / out of range" phrase.
/// </summary>
public class BatteryStatusTextTests
{
    [Fact]
    public void ForState_NotLive_ReturnsOutOfRange()
        => Assert.Equal(BatteryStatusText.OutOfRange, BatteryStatusText.ForState(DeviceState.Unknown));

    [Fact]
    public void ForState_NotLive_IgnoresAnyStaleBatteryFields()
    {
        // Not live, yet battery fields set: they must never be presented as live.
        var stale = new DeviceState { LeftBatteryPercent = 80, IsLive = false };

        Assert.Equal(BatteryStatusText.OutOfRange, BatteryStatusText.ForState(stale));
    }

    [Fact]
    public void ForState_Live_RendersEachComponentWithChargingMark()
    {
        var state = new DeviceState
        {
            IsLive = true,
            LeftBatteryPercent = 80,
            LeftCharging = true,
            RightBatteryPercent = 70,
            CaseBatteryPercent = 100,
            CaseCharging = true,
        };

        Assert.Equal("L 80%⚡ · R 70% · Case 100%⚡", BatteryStatusText.ForState(state));
    }

    [Fact]
    public void ForState_Live_RendersUnknownSentinelPerComponent()
    {
        var state = new DeviceState
        {
            IsLive = true,
            LeftBatteryPercent = null,
            RightBatteryPercent = 50,
            RightCharging = true,
            CaseBatteryPercent = null,
        };

        Assert.Equal("L unknown · R 50%⚡ · Case unknown", BatteryStatusText.ForState(state));
    }

    [Fact]
    public void ForState_AlwaysReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(BatteryStatusText.ForState(DeviceState.Unknown)));
        Assert.False(string.IsNullOrWhiteSpace(
            BatteryStatusText.ForState(new DeviceState { IsLive = true })));
    }
}
