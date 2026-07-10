using PodBridge.Core.Audio;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Core.Tests.Audio;
using PodBridge.Core.Tests.Media;
using PodBridge.Core.Tests.Protocol;
using Xunit;

namespace PodBridge.Core.Tests.Diagnostics;

/// <summary>
/// Device-independent tests for <see cref="DiagnosticsSnapshotFactory"/>: it reads the
/// existing Core seams (no new OS call) and is the first real consumer of
/// <see cref="Capabilities.ICapabilityProvider"/> (issue #53).
/// </summary>
public class DiagnosticsSnapshotFactoryTests
{
    private sealed class FakeBleParseHistory : IBleParseHistory
    {
        public IReadOnlyList<BleParseResult> Recent { get; set; } = [];
    }

    [Fact]
    public void Create_ReflectsCurrentDeviceModelCodecAndDriverPresence()
    {
        var stateProvider = new FakeDeviceStateProvider();
        stateProvider.Publish(new DeviceState { Model = AirPodsModel.AirPodsPro2, IsLive = true });
        var audioReader = new FakeAudioStateReader();
        audioReader.Set(CodecKind.Aac, MicMode.HighQualityA2dp);
        var transport = new FakeAapTransport { IsAvailable = true };
        var registry = new ModelRegistry();
        var capabilityProvider = new PodBridge.Core.Capabilities.CapabilityProvider(registry, transport);
        var history = new FakeBleParseHistory();

        var factory = new DiagnosticsSnapshotFactory(
            stateProvider, audioReader, transport, registry, capabilityProvider, history);

        var snapshot = factory.Create();

        Assert.Equal(AirPodsModel.AirPodsPro2, snapshot.Model);
        Assert.Equal(CodecKind.Aac, snapshot.Codec);
        Assert.True(snapshot.DriverPresent);
        // No host-requestable firmware-version read exists today — always honestly unreadable.
        Assert.Null(snapshot.FirmwareMajor);
    }

    [Fact]
    public void Create_DriverAbsent_ReportsTier1AndHonestReason()
    {
        var stateProvider = new FakeDeviceStateProvider();
        var audioReader = new FakeAudioStateReader();
        var transport = new FakeAapTransport { IsAvailable = false };
        var registry = new ModelRegistry();
        var capabilityProvider = new PodBridge.Core.Capabilities.CapabilityProvider(registry, transport);
        var history = new FakeBleParseHistory();

        var factory = new DiagnosticsSnapshotFactory(
            stateProvider, audioReader, transport, registry, capabilityProvider, history);

        var snapshot = factory.Create();

        Assert.False(snapshot.DriverPresent);
        Assert.Equal("Tier 1 (driver-free)", snapshot.Tier);
    }
}
