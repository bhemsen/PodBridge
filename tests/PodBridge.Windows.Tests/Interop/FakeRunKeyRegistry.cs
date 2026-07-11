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
