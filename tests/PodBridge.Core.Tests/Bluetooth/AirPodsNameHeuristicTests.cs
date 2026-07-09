using PodBridge.Core.Bluetooth;
using Xunit;

namespace PodBridge.Core.Tests.Bluetooth;

public class AirPodsNameHeuristicTests
{
    [Theory]
    [InlineData("AirPods Pro")]
    [InlineData("Bendix's AirPods")]
    [InlineData("airpods max")] // case-insensitive
    [InlineData("Beats Fit Pro")]
    [InlineData("Powerbeats")] // contains "beats"
    public void KnownAirPodsOrBeatsNames_Match(string name)
        => Assert.True(AirPodsNameHeuristic.IsMatch(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sony WH-1000XM5")]
    [InlineData("Galaxy Buds")]
    public void OtherOrEmptyNames_DoNotMatch(string? name)
        => Assert.False(AirPodsNameHeuristic.IsMatch(name));
}
