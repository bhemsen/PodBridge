using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using PodBridge.Core.Capabilities;
using PodBridge.Core.Diagnostics;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;
using PodBridge.Windows.Interop;
using Xunit;

namespace PodBridge.Windows.Tests;

/// <summary>
/// Device-independent tests for <see cref="DiagnosticsExporter"/> at the filesystem seam
/// (fake <see cref="IDiagnosticsFileSystem"/>, no real disk write): it writes readable text
/// containing every required field and — structurally, by reflection — never depends on a
/// network-capable type, so it can perform no network call (issue #54; constitution:
/// local-only).
/// </summary>
public sealed class DiagnosticsExporterTests
{
    private sealed class FakeFileSystem : IDiagnosticsFileSystem
    {
        public string Directory { get; } = @"C:\fake\diagnostics";
        public List<(string Path, string Content)> Writes { get; } = [];

        public string GetExportDirectory() => Directory;

        public void WriteAllText(string path, string content) => Writes.Add((path, content));
    }

    // Minimal IAapTransport stub — only IsAvailable is consulted by CapabilityProvider for
    // this snapshot fixture; the send/receive path is never exercised here.
    private sealed class FakeAapTransportForExportTests : IAapTransport
    {
        public bool IsAvailable { get; set; }

        public event EventHandler<ReadOnlyMemory<byte>>? PacketReceived { add { } remove { } }

        public event EventHandler? Connected { add { } remove { } }

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // A fixed clock so the exported filename/timestamp is deterministic in tests, without
    // adding a test-time-provider package dependency for one call site.
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DiagnosticsSnapshot Snapshot() => DiagnosticsSnapshotBuilder.Build(
        new ModelRegistry().Resolve(AirPodsModel.AirPodsPro2),
        firmwareMajor: null,
        PodBridge.Core.Audio.CodecKind.Aac,
        driverPresent: true,
        new CapabilityProvider(new ModelRegistry(), new FakeAapTransportForExportTests { IsAvailable = true }),
        []);

    [Fact]
    public void Export_WritesReadableTextToTheExportDirectory_AndReturnsTheSameText()
    {
        var fileSystem = new FakeFileSystem();
        var exporter = new DiagnosticsExporter(fileSystem, new FixedTimeProvider(DateTimeOffset.Parse(
            "2026-07-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));

        var result = exporter.Export(Snapshot());

        var write = Assert.Single(fileSystem.Writes);
        Assert.StartsWith(fileSystem.Directory, write.Path, StringComparison.Ordinal);
        Assert.Equal(write.Path, result.FilePath);
        Assert.Equal(write.Content, result.Text);
        Assert.Contains("AirPods Pro 2", result.Text, StringComparison.Ordinal);
        Assert.Contains("Codec: Aac", result.Text, StringComparison.Ordinal);
        Assert.Contains("Tier: Tier 2", result.Text, StringComparison.Ordinal);
        Assert.Contains("Driver present: True", result.Text, StringComparison.Ordinal);
        Assert.Contains("Capability matrix:", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsExporter_DeclaresNoNetworkCapableDependency()
    {
        // Structural proof of "the writer performs no network call": neither its fields nor
        // any constructor parameter is a network-capable type (HttpClient, sockets, etc.).
        var forbidden = new[] { typeof(HttpClient), typeof(Socket), typeof(WebClient) };
        var type = typeof(DiagnosticsExporter);

        foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.DoesNotContain(field.FieldType, forbidden);
        }

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                Assert.DoesNotContain(parameter.ParameterType, forbidden);
            }
        }
    }
}
