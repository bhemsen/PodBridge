using System.IO;
using PodBridge.Core.Models;
using PodBridge.Windows;
using Xunit;

namespace PodBridge.Windows.Tests;

/// <summary>
/// Round-trip tests for the file-backed <see cref="GestureConfigStore"/> using a temp file
/// (no %LOCALAPPDATA% writes). Confirms the user's per-bud choice persists across a
/// store instance and that a missing / unreadable / malformed value reads back as
/// <see langword="null"/> ("no assignment yet"), so the re-push policy never sends an
/// unsolicited config (issue #48; spec docs/specs/spec-gesture-remap.md).
/// </summary>
public sealed class GestureConfigStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"podbridge-gesture-{Guid.NewGuid():N}.txt");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Theory]
    [InlineData(GestureAction.NoiseControl, GestureAction.Siri)]
    [InlineData(GestureAction.Siri, GestureAction.NoiseControl)]
    [InlineData(GestureAction.NoiseControl, GestureAction.NoiseControl)]
    public void Save_then_Load_round_trips_the_per_bud_configuration(
        GestureAction right, GestureAction left)
    {
        var configuration = new GestureConfiguration(right, left);

        new GestureConfigStore(_path).Save(configuration);
        var loaded = new GestureConfigStore(_path).Load(); // a fresh instance reads it back

        Assert.Equal(configuration, loaded);
    }

    [Fact]
    public void Load_returns_null_when_the_file_is_absent()
        => Assert.Null(new GestureConfigStore(_path).Load());

    [Fact]
    public void Load_returns_null_for_a_malformed_value()
    {
        File.WriteAllText(_path, "NoiseControl;NotAnAction");

        Assert.Null(new GestureConfigStore(_path).Load());
    }

    [Fact]
    public void Load_returns_null_for_a_value_with_the_wrong_shape()
    {
        File.WriteAllText(_path, "NoiseControl"); // only one field, not right;left

        Assert.Null(new GestureConfigStore(_path).Load());
    }

    [Fact]
    public void Save_overwrites_a_previous_choice()
    {
        var store = new GestureConfigStore(_path);
        store.Save(GestureConfiguration.Shared(GestureAction.Siri));

        store.Save(Config());

        Assert.Equal(Config(), new GestureConfigStore(_path).Load());
    }

    private static GestureConfiguration Config()
        => new(RightBud: GestureAction.NoiseControl, LeftBud: GestureAction.Siri);
}
