using PodBridge.Core.Startup;

namespace PodBridge.Core.Tests.Startup;

/// <summary>
/// Device-independent stand-in for the MSIX <c>StartupTask</c> adapter. Starts
/// <see cref="StartupToggleState.Disabled"/> (auto-start is opt-in and OFF by
/// default) and honours the real API's contract: a request to enable is refused
/// while the state is a user/policy block (the app cannot override it), and
/// disabling always turns auto-start off. Call counts let a test assert the toggle
/// actually drove the adapter (Tier-1 test gate — no physical device).
/// </summary>
internal sealed class FakeStartupToggle : IStartupToggle
{
    public FakeStartupToggle(StartupToggleState initial = StartupToggleState.Disabled)
        => State = initial;

    public StartupToggleState State { get; private set; }

    public int RequestEnableCallCount { get; private set; }

    public int DisableCallCount { get; private set; }

    public Task<StartupToggleState> GetStateAsync() => Task.FromResult(State);

    public Task<StartupToggleState> RequestEnableAsync()
    {
        RequestEnableCallCount++;

        // A user/policy disable is never overridden (mirrors the WinRT API).
        if (State is not (StartupToggleState.DisabledByUser or StartupToggleState.DisabledByPolicy))
        {
            State = StartupToggleState.Enabled;
        }

        return Task.FromResult(State);
    }

    public Task<StartupToggleState> DisableAsync()
    {
        DisableCallCount++;
        State = StartupToggleState.Disabled;
        return Task.FromResult(State);
    }
}
