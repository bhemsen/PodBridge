namespace PodBridge.Core.Startup;

/// <summary>
/// OS-free control surface over the opt-in "start PodBridge at sign-in" option.
/// Tier 1: no driver, no admin, no elevation. Implemented on Windows by
/// <c>RunKeyStartupToggle</c> over the per-user <c>HKCU\...\Run</c> key; the About
/// surface reads and sets it. Auto-start is <b>opt-in and off by default</b>; the
/// user's Settings / Task-Manager disable always wins and is reported honestly as
/// <see cref="StartupToggleState.DisabledByUser"/> — the app never overrides it.
/// Kept in Core (behind this interface) so the toggle contract is covered by the
/// Tier-1 device-independent test gate with a fake.
/// </summary>
public interface IStartupToggle
{
    /// <summary>
    /// Reads the current auto-start state. Never throws; degrades to
    /// <see cref="StartupToggleState.Disabled"/> when the task is unavailable
    /// (e.g. an unpackaged run with no package identity).
    /// </summary>
    Task<StartupToggleState> GetStateAsync();

    /// <summary>
    /// Requests that auto-start be enabled and returns the resulting state. For a
    /// packaged desktop app this enables silently (no consent dialog); a user /
    /// policy disable is not overridden, so the result may still be
    /// <see cref="StartupToggleState.DisabledByUser"/> or
    /// <see cref="StartupToggleState.DisabledByPolicy"/>. Never throws.
    /// </summary>
    Task<StartupToggleState> RequestEnableAsync();

    /// <summary>
    /// Turns auto-start off and returns the resulting state
    /// (<see cref="StartupToggleState.Disabled"/>). Never throws.
    /// </summary>
    Task<StartupToggleState> DisableAsync();
}
