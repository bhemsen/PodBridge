using PodBridge.Core.Audio;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Diagnostics;

/// <summary>
/// Device-independent tests for <see cref="DiagnosticsSnapshotFormatter"/>: readable text
/// carrying every field the spec requires, deterministic for a fixed
/// <c>generatedAt</c>, and never a full Bluetooth address.
/// </summary>
public class DiagnosticsSnapshotFormatterTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticsSnapshot Snapshot(bool driverPresent) => DiagnosticsSnapshotBuilder.Build(
        new ModelRegistry().Resolve(AirPodsModel.AirPodsPro2),
        firmwareMajor: null,
        CodecKind.Aac,
        driverPresent,
        new PodBridge.Core.Capabilities.CapabilityProvider(
            new ModelRegistry(), new FakeAapTransport { IsAvailable = driverPresent }),
        [new BleParseResult { ParsedSuccessfully = true, MaskedAddress = "**:**:**:**:9A:BC", Model = AirPodsModel.AirPodsPro2 }]);

    [Fact]
    public void Render_IsDeterministic_ForTheSameSnapshotAndTimestamp()
    {
        var snapshot = Snapshot(driverPresent: true);

        var first = DiagnosticsSnapshotFormatter.Render(snapshot, FixedTime);
        var second = DiagnosticsSnapshotFormatter.Render(snapshot, FixedTime);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Render_ContainsEveryRequiredField()
    {
        var text = DiagnosticsSnapshotFormatter.Render(Snapshot(driverPresent: true), FixedTime);

        Assert.Contains("AirPods Pro 2", text, StringComparison.Ordinal);
        Assert.Contains("Firmware major:", text, StringComparison.Ordinal);
        Assert.Contains("Codec: Aac", text, StringComparison.Ordinal);
        Assert.Contains("Tier: Tier 2", text, StringComparison.Ordinal);
        Assert.Contains("Driver present: True", text, StringComparison.Ordinal);
        Assert.Contains("Driver signing/test-mode status:", text, StringComparison.Ordinal);
        Assert.Contains("Capability matrix:", text, StringComparison.Ordinal);
        Assert.Contains("Tier1.CaseBattery", text, StringComparison.Ordinal);
        Assert.Contains("Tier2.NoiseControl", text, StringComparison.Ordinal);
        Assert.Contains("Recent BLE parse results", text, StringComparison.Ordinal);
        Assert.Contains("**:**:**:**:9A:BC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NeverContainsAFullBluetoothAddress()
    {
        var text = DiagnosticsSnapshotFormatter.Render(Snapshot(driverPresent: false), FixedTime);

        // The only address form present is the masked one; a full 12-hex-digit MAC never appears.
        Assert.DoesNotContain("123456789ABC", text, StringComparison.OrdinalIgnoreCase);
    }
}
