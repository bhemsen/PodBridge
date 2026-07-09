using PodBridge.Core.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Protocol;

public class AppleContinuityTests
{
    [Fact]
    public void AppleCompanyId_IsRecognized()
        => Assert.True(AppleContinuity.IsAppleManufacturerData(AppleContinuity.AppleCompanyId));

    [Theory]
    [InlineData((ushort)0x0006)] // Microsoft
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0x00E0)] // Google
    public void NonAppleCompanyId_IsRejected(ushort companyId)
        => Assert.False(AppleContinuity.IsAppleManufacturerData(companyId));
}
