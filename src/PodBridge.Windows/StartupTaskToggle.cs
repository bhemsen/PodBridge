using PodBridge.Core.Startup;
using Windows.ApplicationModel;

namespace PodBridge.Windows;

/// <summary>
/// WinRT implementation of <see cref="IStartupToggle"/> over the MSIX
/// <see cref="StartupTask"/> API. Drives the <c>windows.startupTask</c> extension
/// declared in <c>Package.appxmanifest</c> (TaskId <see cref="TaskId"/>,
/// <c>Enabled="false"</c> so auto-start is opt-in and off by default). Tier 1:
/// pure WinRT projection, no driver, no admin — a packaged desktop app enables
/// silently (no consent dialog) and the user's Settings / Task-Manager disable is
/// never overridden (docs/research/msix-packaging.md, source 4).
/// </summary>
/// <remarks>
/// <see cref="StartupTask.GetAsync"/> requires package identity, so every call
/// degrades gracefully to <see cref="StartupToggleState.Disabled"/> when it throws
/// (e.g. an unpackaged dev run). Stateless: it fetches the task fresh per call and
/// holds no handle between calls, so it is registered transient.
/// </remarks>
public sealed class StartupTaskToggle : IStartupToggle
{
    // Must match the manifest's <uap5:StartupTask TaskId="..."> value.
    private const string TaskId = "PodBridgeStartup";

    /// <inheritdoc />
    public async Task<StartupToggleState> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId).AsTask().ConfigureAwait(false);
            return Map(task.State);
        }
        catch (Exception)
        {
            return StartupToggleState.Disabled;
        }
    }

    /// <inheritdoc />
    public async Task<StartupToggleState> RequestEnableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId).AsTask().ConfigureAwait(false);
            var state = await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
            return Map(state);
        }
        catch (Exception)
        {
            return StartupToggleState.Disabled;
        }
    }

    /// <inheritdoc />
    public async Task<StartupToggleState> DisableAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId).AsTask().ConfigureAwait(false);
            task.Disable();
            return Map(task.State);
        }
        catch (Exception)
        {
            return StartupToggleState.Disabled;
        }
    }

    private static StartupToggleState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupToggleState.Enabled,
        StartupTaskState.DisabledByUser => StartupToggleState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupToggleState.DisabledByPolicy,
        _ => StartupToggleState.Disabled,
    };
}
