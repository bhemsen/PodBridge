using PodBridge.Core.Startup;
using Xunit;

namespace PodBridge.Core.Tests.Startup;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the opt-in
/// auto-start-at-login contract (issue #35). They drive the toggle through the
/// <see cref="IStartupToggle"/> abstraction with a fake — no physical device, no
/// package identity — and assert the invariants the real MSIX <c>StartupTask</c>
/// adapter must honour: default OFF, enable → registered, disable → cleared, and
/// that a user disable is reported honestly rather than silently overridden.
/// </summary>
public class StartupToggleTests
{
    [Fact]
    public async Task DefaultState_IsDisabled_AutoStartIsOptInAndOff()
    {
        var toggle = new FakeStartupToggle();

        Assert.Equal(StartupToggleState.Disabled, await toggle.GetStateAsync());
    }

    [Fact]
    public async Task RequestEnable_RegistersAutoStart()
    {
        var toggle = new FakeStartupToggle();

        var result = await toggle.RequestEnableAsync();

        Assert.Equal(StartupToggleState.Enabled, result);
        Assert.Equal(StartupToggleState.Enabled, await toggle.GetStateAsync());
        Assert.Equal(1, toggle.RequestEnableCallCount);
    }

    [Fact]
    public async Task Disable_ClearsAutoStart()
    {
        var toggle = new FakeStartupToggle(StartupToggleState.Enabled);

        var result = await toggle.DisableAsync();

        Assert.Equal(StartupToggleState.Disabled, result);
        Assert.Equal(StartupToggleState.Disabled, await toggle.GetStateAsync());
        Assert.Equal(1, toggle.DisableCallCount);
    }

    [Fact]
    public async Task RequestEnable_DoesNotOverrideAUserDisable()
    {
        var toggle = new FakeStartupToggle(StartupToggleState.DisabledByUser);

        var result = await toggle.RequestEnableAsync();

        // The user's Settings/Task-Manager disable wins; the app reports it
        // honestly instead of pretending auto-start was turned on.
        Assert.Equal(StartupToggleState.DisabledByUser, result);
    }
}
