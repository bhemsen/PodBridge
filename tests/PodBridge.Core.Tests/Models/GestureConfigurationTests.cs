using PodBridge.Core.Models;
using Xunit;

namespace PodBridge.Core.Tests.Models;

/// <summary>
/// Device-independent tests for the Core gesture-configuration model — the per-bud
/// press-and-hold action map (docs/research/gesture-aap.md). Confirms the shared-fallback
/// factory and that only research-confirmed actions are representable.
/// </summary>
public class GestureConfigurationTests
{
    [Fact]
    public void Shared_AssignsTheSameActionToBothBuds()
    {
        var config = GestureConfiguration.Shared(GestureAction.NoiseControl);
        Assert.Equal(GestureAction.NoiseControl, config.RightBud);
        Assert.Equal(GestureAction.NoiseControl, config.LeftBud);
        Assert.True(config.IsShared);
    }

    [Fact]
    public void IsShared_FalseForIndependentPerBudAssignment()
    {
        var config = new GestureConfiguration(
            RightBud: GestureAction.NoiseControl, LeftBud: GestureAction.Siri);
        Assert.False(config.IsShared);
    }

    [Fact]
    public void RecordEquality_ComparesByBudAssignment()
    {
        var a = new GestureConfiguration(GestureAction.Siri, GestureAction.NoiseControl);
        var b = new GestureConfiguration(GestureAction.Siri, GestureAction.NoiseControl);
        var swapped = new GestureConfiguration(GestureAction.NoiseControl, GestureAction.Siri);
        Assert.Equal(a, b);
        Assert.NotEqual(a, swapped); // right/left are not interchangeable
    }

    [Theory]
    [InlineData(GestureAction.NoiseControl, 0x01)]
    [InlineData(GestureAction.Siri, 0x05)]
    public void GestureAction_ValuesEqualDocumentedWireBytes(GestureAction action, byte wireByte)
        => Assert.Equal(wireByte, (byte)action); // docs/research/gesture-aap.md action enum
}
