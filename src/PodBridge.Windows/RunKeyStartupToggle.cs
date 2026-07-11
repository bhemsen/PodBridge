using PodBridge.Core.Startup;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Portable per-user implementation of <see cref="IStartupToggle"/> over the
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> key (issue #117), replacing
/// the MSIX <c>StartupTaskToggle</c> now that the app ships as a self-contained exe with
/// no package identity. Tier 1: per-user HKCU, no admin, no driver. Auto-start is
/// opt-in and off by default — <see cref="GetStateAsync"/> reports
/// <see cref="StartupToggleState.Disabled"/> until <see cref="RequestEnableAsync"/> is called.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stale-path self-heal:</b> a portable install can be moved to a new folder; on every
/// read while the toggle is Enabled, the stored command line is compared to the current
/// <see cref="Environment.ProcessPath"/> and silently rewritten on a mismatch
/// (docs/research/release-1.0.md §4). This runs on the first <see cref="GetStateAsync"/>
/// call after construction (the About surface's read on load) as well as every subsequent
/// read, so it is both a "first read" and an "every launch" self-heal.
/// </para>
/// <para>
/// <b>User-disable respect:</b> disabling the entry from Task Manager's Startup tab writes
/// a flag under the sibling <c>StartupApproved\Run</c> key rather than deleting the Run
/// value; <see cref="IRunKeyRegistry.IsDisabledByUser"/> reads it and this adapter reports
/// <see cref="StartupToggleState.DisabledByUser"/> without ever rewriting the flag or the
/// Run value to force it back on.
/// </para>
/// <para>
/// <b>Never throws:</b> per the <see cref="IStartupToggle"/> contract, every method degrades
/// to <see cref="StartupToggleState.Disabled"/> instead of propagating a registry exception
/// (e.g. <see cref="UnauthorizedAccessException"/> on a policy/ACL-restricted HKCU hive),
/// mirroring the MSIX <c>StartupTaskToggle</c> it replaces.
/// </para>
/// Stateless: it opens the registry fresh per call and holds no handle between calls, like
/// the MSIX toggle it replaces, so it is registered transient.
/// </remarks>
public sealed class RunKeyStartupToggle : IStartupToggle
{
    private readonly IRunKeyRegistry _registry;
    private readonly Func<string?> _getProcessPath;

    /// <summary>Production constructor: reads/writes the real per-user HKCU Run key.</summary>
    public RunKeyStartupToggle()
        : this(new Win32RunKeyRegistry(), static () => Environment.ProcessPath)
    {
    }

    // Test seam: PodBridge.Windows.Tests substitutes a fake registry + a fixed process path
    // so enable/disable/self-heal/DisabledByUser are exercised with no real registry write.
    internal RunKeyStartupToggle(IRunKeyRegistry registry, Func<string?> getProcessPath)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(getProcessPath);
        _registry = registry;
        _getProcessPath = getProcessPath;
    }

    /// <inheritdoc />
    public Task<StartupToggleState> GetStateAsync()
    {
        try
        {
            return Task.FromResult(ReadState());
        }
        catch (Exception)
        {
            // Never throws (IStartupToggle contract): a policy/ACL-restricted HKCU Run key
            // degrades to Disabled instead of crashing the caller (issue #117 review).
            return Task.FromResult(StartupToggleState.Disabled);
        }
    }

    /// <inheritdoc />
    public Task<StartupToggleState> RequestEnableAsync()
    {
        try
        {
            var processPath = _getProcessPath();
            if (!string.IsNullOrEmpty(processPath))
            {
                _registry.SetRunValue(Quote(processPath));
            }

            return Task.FromResult(ReadState());
        }
        catch (Exception)
        {
            return Task.FromResult(StartupToggleState.Disabled);
        }
    }

    /// <inheritdoc />
    public Task<StartupToggleState> DisableAsync()
    {
        try
        {
            _registry.DeleteRunValue();
            return Task.FromResult(StartupToggleState.Disabled);
        }
        catch (Exception)
        {
            return Task.FromResult(StartupToggleState.Disabled);
        }
    }

    private StartupToggleState ReadState()
    {
        var stored = _registry.GetRunValue();
        if (stored is null)
        {
            return StartupToggleState.Disabled;
        }

        if (_registry.IsDisabledByUser())
        {
            return StartupToggleState.DisabledByUser;
        }

        SelfHealIfStale(stored);
        return StartupToggleState.Enabled;
    }

    private void SelfHealIfStale(string stored)
    {
        var processPath = _getProcessPath();
        if (string.IsNullOrEmpty(processPath))
        {
            return;
        }

        var expected = Quote(processPath);
        if (!string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase))
        {
            _registry.SetRunValue(expected);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
