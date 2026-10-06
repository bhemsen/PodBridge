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

    [Theory]
    [InlineData("AirPods Pro")]
    [InlineData("Headphones (AirPods Pro (Anton))")] // an audio endpoint's friendly name
    [InlineData("airpods max")] // case-insensitive
    public void AirPodsNames_AreAirPodsNames(string name)
        => Assert.True(AirPodsNameHeuristic.IsAirPodsName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Beats Fit Pro")]
    [InlineData("Headphones (Powerbeats Pro)")]
    [InlineData("Sony WH-1000XM5")]
    public void BeatsOtherOrEmptyNames_AreNotAirPodsNames(string? name)
        => Assert.False(AirPodsNameHeuristic.IsAirPodsName(name));
}
