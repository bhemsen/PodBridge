using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PodBridge.Windows.Logging;
using Xunit;

// CA1848/CA1873 push production code toward LoggerMessage source-generated delegates for
// hot-path performance; this test file exercises the sink with a handful of plain log
// calls, where that ceremony would add nothing but noise.
#pragma warning disable CA1848, CA1873

namespace PodBridge.Windows.Tests.Logging;

/// <summary>
/// Device-independent tests for <see cref="RollingFileLoggerProvider"/>: the local file is
/// the only sink, verbosity respects <see cref="RollingFileLoggerProvider.MinLevel"/> (the
/// tray Debug toggle), the active file rolls once it exceeds the size cap, and aged rolled
/// files are purged (spec: "~10 MB / 7 days"; constitution: local-only, no network sink).
/// Uses a real temp directory (small, deterministic files) — mirrors the
/// <c>GestureConfigStoreTests</c> pattern of real file I/O with no shared state.
/// </summary>
public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"podbridge-logs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Information_IsTheDefaultMinLevel_DebugIsDroppedUntilOptedIn()
    {
        var provider = new RollingFileLoggerProvider(_directory);
        var logger = provider.CreateLogger("Test");

        logger.LogDebug("should not appear");
        logger.LogInformation("should appear");
        provider.Dispose(); // release the writer handle before reading the file back

        var text = File.ReadAllText(ActiveFilePath());
        Assert.DoesNotContain("should not appear", text, StringComparison.Ordinal);
        Assert.Contains("should appear", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugToggle_RaisesVerbosity_ToTheLocalFileOnly()
    {
        var provider = new RollingFileLoggerProvider(_directory);
        var logger = provider.CreateLogger("Test");

        provider.MinLevel = LogLevel.Debug;
        logger.LogDebug("now visible after the toggle");
        provider.Dispose(); // release the writer handle before reading the file back

        var text = File.ReadAllText(ActiveFilePath());
        Assert.Contains("now visible after the toggle", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveFile_Rolls_WhenItExceedsTheSizeCap()
    {
        using var provider = new RollingFileLoggerProvider(_directory, maxBytes: 200);
        var logger = provider.CreateLogger("Test");

        for (var i = 0; i < 50; i++)
        {
            logger.LogInformation("padding line {Index} to exceed the tiny size cap", i);
        }

        var rolledFiles = Directory.EnumerateFiles(_directory, "podbridge-*.log").ToList();
        Assert.NotEmpty(rolledFiles); // at least one roll happened
        Assert.True(new FileInfo(ActiveFilePath()).Length < 5000); // active file stayed small
    }

    [Fact]
    public void AgedRolledFiles_ArePurged_ButFreshOnesSurvive()
    {
        Directory.CreateDirectory(_directory);
        var oldFile = Path.Combine(_directory, "podbridge-20000101000000000.log");
        var freshFile = Path.Combine(_directory, "podbridge-20990101000000000.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(freshFile, "fresh");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(30));
        File.SetLastWriteTimeUtc(freshFile, DateTime.UtcNow);

        // Construction purges aged rolled files (maxAge defaults to 7 days).
        using var provider = new RollingFileLoggerProvider(_directory, maxAge: TimeSpan.FromDays(7));

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(freshFile));
    }

    [Fact]
    public void RollingFileLoggerProvider_DeclaresNoNetworkCapableDependency()
    {
        var forbidden = new[] { typeof(HttpClient), typeof(Socket), typeof(WebClient) };
        var type = typeof(RollingFileLoggerProvider);

        foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.DoesNotContain(field.FieldType, forbidden);
        }

        foreach (var ctor in type.GetConstructors())
        {
            foreach (var parameter in ctor.GetParameters())
            {
                Assert.DoesNotContain(parameter.ParameterType, forbidden);
            }
        }
    }

    private string ActiveFilePath() => Path.Combine(_directory, "podbridge.log");
}
