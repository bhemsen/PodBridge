using PodBridge.Windows.Interop;

namespace PodBridge.Windows.Tests.Interop;

/// <summary>
/// Device-independent stand-in for the HKCU registry seam. Lets a test drive
/// <see cref="RunKeyStartupToggle"/>'s enable / disable / self-heal / DisabledByUser logic
/// with no real registry read or write.
/// </summary>
internal sealed class FakeRunKeyRegistry : IRunKeyRegistry
{
    /// <summary>The currently stored Run value, or <see langword="null"/> when absent.</summary>
    public string? RunValue { get; set; }

    /// <summary>When set, <see cref="IsDisabledByUser"/> reports the user's Task-Manager disable.</summary>
    public bool DisabledByUserFlag { get; set; }

    /// <summary>Number of times <see cref="SetRunValue"/> was called (self-heal checks).</summary>
    public int SetCount { get; private set; }

    public string? GetRunValue() => RunValue;

    public void SetRunValue(string commandLine)
    {
        RunValue = commandLine;
        SetCount++;
    }

    public void DeleteRunValue() => RunValue = null;

    public bool IsDisabledByUser() => DisabledByUserFlag;
}

/// <summary>
/// Registry seam that throws on every member, standing in for a policy/ACL-restricted HKCU
/// hive (e.g. a managed "Windows work laptop" profile). Used to verify
/// <see cref="RunKeyStartupToggle"/> honours the <see cref="PodBridge.Core.Startup.IStartupToggle"/>
/// contract's "never throws" guarantee instead of letting a registry exception escape
/// (issue #117 review).
/// </summary>
internal sealed class ThrowingRunKeyRegistry : IRunKeyRegistry
{
    public string? GetRunValue() => throw new UnauthorizedAccessException("Access to the registry key is denied.");

    public void SetRunValue(string commandLine) => throw new UnauthorizedAccessException("Access to the registry key is denied.");

    public void DeleteRunValue() => throw new UnauthorizedAccessException("Access to the registry key is denied.");

    public bool IsDisabledByUser() => throw new UnauthorizedAccessException("Access to the registry key is denied.");
}
