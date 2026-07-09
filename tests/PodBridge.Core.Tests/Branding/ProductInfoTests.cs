using PodBridge.Core.Branding;
using Xunit;

namespace PodBridge.Core.Tests.Branding;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) guarding the branding /
/// disclaimer / license invariant carried by the About surface: the coined product
/// name contains neither "Apple" nor "AirPods", the mandatory not-affiliated
/// disclaimer is present, the declared license is Apache-2.0, and the audio note
/// never claims Apple-parity sound.
/// </summary>
public class ProductInfoTests
{
    [Theory]
    [InlineData("Apple")]
    [InlineData("AirPods")]
    public void Name_DoesNotContainTrademarkedTerm(string forbidden)
        => Assert.DoesNotContain(forbidden, ProductInfo.Name, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Name_IsTheCoinedProductName()
        => Assert.Equal("PodBridge", ProductInfo.Name);

    [Fact]
    public void Disclaimer_IsPresentAndStatesNotAffiliated()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProductInfo.Disclaimer));
        Assert.Contains("not affiliated", ProductInfo.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeclaredLicense_IsApache2()
        => Assert.Equal("Apache-2.0", ProductInfo.LicenseId);

    [Fact]
    public void Descriptor_UsesForAirPodsDescriptively()
        => Assert.Contains("for AirPods", ProductInfo.Descriptor, StringComparison.Ordinal);

    [Fact]
    public void AudioNote_IsHonestAndNeverClaimsAppleParity()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProductInfo.AudioNote));
        Assert.Contains("never claims", ProductInfo.AudioNote, StringComparison.OrdinalIgnoreCase);
    }
}
