using PodBridge.Core.Startup;
using PodBridge.Windows;
using PodBridge.Windows.Tests.Interop;
using Xunit;

namespace PodBridge.Windows.Tests;

/// <summary>
/// Device-independent tests for <see cref="RunKeyStartupToggle"/> at the HKCU registry seam
/// (fake interop). They cover the default-OFF contract, enable writing the quoted current
/// process path, disable clearing the value, the stale-path self-heal, and honouring a
/// user's Task-Manager disable without ever overriding it (issue #117).
/// </summary>
public sealed class RunKeyStartupToggleTests
{
    private const string CurrentPath = @"C:\Apps\PodBridge\PodBridge.exe";
    private const string QuotedCurrentPath = "\"" + CurrentPath + "\"";

    [Fact]
    public async Task GetStateAsync_defaults_to_Disabled_when_no_Run_value_exists()
    {
        var registry = new FakeRunKeyRegistry();
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.GetStateAsync();

        Assert.Equal(StartupToggleState.Disabled, state);
    }

    [Fact]
    public async Task RequestEnableAsync_writes_the_quoted_current_process_path()
    {
        var registry = new FakeRunKeyRegistry();
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.RequestEnableAsync();

        Assert.Equal(StartupToggleState.Enabled, state);
        Assert.Equal(QuotedCurrentPath, registry.RunValue);
    }

    [Fact]
    public async Task DisableAsync_clears_the_Run_value()
    {
        var registry = new FakeRunKeyRegistry { RunValue = QuotedCurrentPath };
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.DisableAsync();

        Assert.Equal(StartupToggleState.Disabled, state);
        Assert.Null(registry.RunValue);
    }

    [Fact]
    public async Task GetStateAsync_self_heals_a_stale_path_while_Enabled()
    {
        var registry = new FakeRunKeyRegistry { RunValue = "\"C:\\OldLocation\\PodBridge.exe\"" };
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.GetStateAsync();

        Assert.Equal(StartupToggleState.Enabled, state);
        Assert.Equal(QuotedCurrentPath, registry.RunValue); // silently rewritten
        Assert.Equal(1, registry.SetCount);
    }

    [Fact]
    public async Task GetStateAsync_does_not_rewrite_an_already_current_path()
    {
        var registry = new FakeRunKeyRegistry { RunValue = QuotedCurrentPath };
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        await toggle.GetStateAsync();

        Assert.Equal(0, registry.SetCount); // nothing to heal -> no unnecessary write
    }

    [Fact]
    public async Task GetStateAsync_reports_DisabledByUser_and_does_not_self_heal_or_delete()
    {
        var registry = new FakeRunKeyRegistry
        {
            RunValue = "\"C:\\OldLocation\\PodBridge.exe\"",
            DisabledByUserFlag = true,
        };
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.GetStateAsync();

        Assert.Equal(StartupToggleState.DisabledByUser, state);
        Assert.Equal("\"C:\\OldLocation\\PodBridge.exe\"", registry.RunValue); // left untouched
        Assert.Equal(0, registry.SetCount);
    }

    [Fact]
    public async Task RequestEnableAsync_does_not_override_a_users_Task_Manager_disable()
    {
        var registry = new FakeRunKeyRegistry { DisabledByUserFlag = true };
        var toggle = new RunKeyStartupToggle(registry, () => CurrentPath);

        var state = await toggle.RequestEnableAsync();

        // The write still happens (so a later re-enable from Task Manager finds a fresh
        // path), but the reported state honestly reflects the flag, never Enabled.
        Assert.Equal(StartupToggleState.DisabledByUser, state);
        Assert.Equal(QuotedCurrentPath, registry.RunValue);
    }

    [Fact]
    public async Task GetStateAsync_degrades_to_Disabled_when_the_registry_throws()
    {
        var toggle = new RunKeyStartupToggle(new ThrowingRunKeyRegistry(), () => CurrentPath);

        var state = await toggle.GetStateAsync();

        Assert.Equal(StartupToggleState.Disabled, state);
    }

    [Fact]
    public async Task RequestEnableAsync_degrades_to_Disabled_when_the_registry_throws()
    {
        var toggle = new RunKeyStartupToggle(new ThrowingRunKeyRegistry(), () => CurrentPath);

        var state = await toggle.RequestEnableAsync();

        Assert.Equal(StartupToggleState.Disabled, state);
    }

    [Fact]
    public async Task DisableAsync_degrades_to_Disabled_when_the_registry_throws()
    {
        var toggle = new RunKeyStartupToggle(new ThrowingRunKeyRegistry(), () => CurrentPath);

        var state = await toggle.DisableAsync();

        Assert.Equal(StartupToggleState.Disabled, state);
    }
}
