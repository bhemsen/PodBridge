using PodBridge.Core.Audio;
using PodBridge.Core.Capabilities;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Diagnostics;

/// <summary>
/// Device-independent tests for <see cref="DiagnosticsSnapshotBuilder"/>: deterministic
/// given the same inputs, address-masked, secret-free, and honest about driver/signing
/// state (spec docs/specs/spec-model-coverage-hardening.md; constitution local-only).
/// </summary>
public class DiagnosticsSnapshotBuilderTests
{
    private static readonly AirPodsModelInfo Pro2Info = new ModelRegistry().Resolve(AirPodsModel.AirPodsPro2);

    private static readonly BleParseResult SampleParse = new()
    {
        ParsedSuccessfully = true,
        MaskedAddress = "**:**:**:**:9A:BC",
        Model = AirPodsModel.AirPodsPro2,
    };

    private static CapabilityProvider Provider(bool driverPresent)
        => new(new ModelRegistry(), new FakeAapTransport { IsAvailable = driverPresent });

    [Fact]
    public void Build_IsDeterministic_GivenTheSameInputs()
    {
        var first = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Aac, driverPresent: true, Provider(true), [SampleParse]);
        var second = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Aac, driverPresent: true, Provider(true), [SampleParse]);

        Assert.Equal(first.Model, second.Model);
        Assert.Equal(first.ModelDisplayName, second.ModelDisplayName);
        Assert.Equal(first.FirmwareMajor, second.FirmwareMajor);
        Assert.Equal(first.Codec, second.Codec);
        Assert.Equal(first.Tier, second.Tier);
        Assert.Equal(first.DriverPresent, second.DriverPresent);
        Assert.Equal(first.DriverSigningStatus, second.DriverSigningStatus);
        Assert.Equal(first.Capabilities, second.Capabilities);
        Assert.Equal(first.RecentBleParses, second.RecentBleParses);
    }

    [Fact]
    public void RecentBleParses_NeverCarriesAFullAddress_OnlyTheMaskedForm()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Unknown, driverPresent: false, Provider(false), [SampleParse]);

        var entry = Assert.Single(snapshot.RecentBleParses);
        Assert.StartsWith("**:**:**:**:", entry.MaskedAddress, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_ContainsNoSecretMarker()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Aac, driverPresent: true, Provider(true), [SampleParse]);

        var rendered = string.Join(
            ' ',
            snapshot.ModelDisplayName,
            snapshot.Tier,
            snapshot.DriverSigningStatus,
            string.Join(' ', snapshot.Capabilities.Select(c => c.Feature + c.Reason)));

        foreach (var forbidden in new[] { "password", "token", "secret", "key=", "apikey" })
        {
            Assert.DoesNotContain(forbidden, rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DriverAbsent_ReportsHonestTierAndSigningStatus()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Unknown, driverPresent: false, Provider(false), []);

        Assert.False(snapshot.DriverPresent);
        Assert.Equal("Tier 1 (driver-free)", snapshot.Tier);
        Assert.Equal(DriverSigningStatus.NoDriverInstalled, snapshot.DriverSigningStatus);
        Assert.Contains(snapshot.Capabilities, c => c.Feature == "Tier2.NoiseControl" && !c.IsAvailable
            && c.Reason == CapabilityDecision.DriverAbsentReason);
    }

    [Fact]
    public void DriverPresent_ReportsHonestTierAndSigningStatus()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Aac, driverPresent: true, Provider(true), []);

        Assert.True(snapshot.DriverPresent);
        Assert.Equal("Tier 2 (advanced tier, driver present)", snapshot.Tier);
        Assert.Equal(DriverSigningStatus.TestSignedDriverPresent, snapshot.DriverSigningStatus);
        // Honest: it plainly says it is NEVER a Microsoft-signed / production-attested driver.
        Assert.Contains("never Microsoft-signed", snapshot.DriverSigningStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityMatrix_CoversEveryTier1AndTier2Feature()
    {
        var snapshot = DiagnosticsSnapshotBuilder.Build(
            Pro2Info, firmwareMajor: null, CodecKind.Aac, driverPresent: true, Provider(true), []);

        foreach (var feature in Enum.GetValues<Tier1Feature>())
        {
            Assert.Contains(snapshot.Capabilities, c => c.Feature == $"Tier1.{feature}");
        }

        foreach (var feature in Enum.GetValues<Tier2Feature>())
        {
            Assert.Contains(snapshot.Capabilities, c => c.Feature == $"Tier2.{feature}");
        }
    }
}
