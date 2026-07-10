using PodBridge.Windows.Interop;

namespace PodBridge.Windows.Tests.Interop;

/// <summary>
/// Device-independent stand-in for the install-script locator: a test decides whether the
/// driver package "is present" (via <see cref="ScriptPath"/>) so <c>AdvancedTierInstaller</c>'s
/// locate → launch / guide-to-docs decision runs with no filesystem.
/// </summary>
internal sealed class FakeInstallScriptLocator : IInstallScriptLocator
{
    /// <summary>Path to report; <see langword="null"/> == driver package not present locally.</summary>
    public string? ScriptPath { get; set; } = @"C:\pkg\install-advanced-tier.ps1";

    public string? Locate() => ScriptPath;
}

/// <summary>
/// Device-independent stand-in for the elevated launcher: records what would be launched and
/// lets a test simulate the user approving (<see cref="Result"/> = true) or declining the UAC
/// prompt. It never starts a process, so no elevation ever happens in tests.
/// </summary>
internal sealed class FakeElevatedProcessLauncher : IElevatedProcessLauncher
{
    /// <summary>What <see cref="Launch"/> returns — true == user approved, false == declined.</summary>
    public bool Result { get; set; } = true;

    /// <summary>The spec passed to the most recent <see cref="Launch"/>.</summary>
    public ElevatedLaunchSpec? LastSpec { get; private set; }

    /// <summary>Number of times <see cref="Launch"/> was called.</summary>
    public int LaunchCount { get; private set; }

    public bool Launch(ElevatedLaunchSpec spec)
    {
        LastSpec = spec;
        LaunchCount++;
        return Result;
    }
}
