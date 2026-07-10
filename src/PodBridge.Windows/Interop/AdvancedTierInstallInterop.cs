using System.ComponentModel;
using System.Diagnostics;

namespace PodBridge.Windows.Interop;

// Win32 seam for the advanced-tier install step (issue #45). AdvancedTierInstaller reaches
// the OS ONLY through the two internal interfaces below; the concrete implementations do the
// real filesystem probe + elevated ShellExecute, and the fakes in PodBridge.Windows.Tests
// substitute at this seam so the installer's decision logic (locate script -> launch
// elevated / guide to docs; never elevate the app) is device-independent. The real elevated
// install + test-signing + hardware load is the documented human smoke test.

/// <summary>
/// Locates the shipped-separately advanced-tier install script. Returns <see langword="null"/>
/// when the driver package is not present locally (it is never bundled in the app MSIX), which
/// the installer maps to <c>PackageMissing</c> so the App guides the user to the docs.
/// </summary>
internal interface IInstallScriptLocator
{
    string? Locate();
}

/// <summary>
/// Launches a process ELEVATED (ShellExecute verb "runas" — a UAC prompt). Returns
/// <see langword="false"/> when the user declines the prompt. Implementations launch ONLY the
/// named tool (PowerShell running the install script), never the calling app, so the
/// <c>asInvoker</c> app is never elevated (constitution: opt-in invasiveness).
/// </summary>
internal interface IElevatedProcessLauncher
{
    bool Launch(ElevatedLaunchSpec spec);
}

/// <summary>What to launch elevated: an executable and its command line.</summary>
internal readonly record struct ElevatedLaunchSpec(string FileName, string Arguments);

/// <summary>
/// Real locator: probes the documented candidate folders for the install script — an override
/// env var, the per-user PodBridge data folder, and a folder next to the app — in that order.
/// </summary>
internal sealed class DefaultInstallScriptLocator : IInstallScriptLocator
{
    // The advanced-tier install/uninstall script (driver/PodBridgeAAP/install-advanced-tier.ps1).
    internal const string ScriptName = "install-advanced-tier.ps1";

    // Env override so a user who extracted the driver package elsewhere can point at it, plus
    // the two documented default locations (docs/user/advanced-tier.md).
    internal const string DirEnvVar = "PODBRIDGE_ADVANCED_TIER_DIR";

    private readonly Func<string, bool> _fileExists;
    private readonly IReadOnlyList<string> _candidateDirs;

    public DefaultInstallScriptLocator()
        : this(File.Exists, DefaultCandidateDirs())
    {
    }

    internal DefaultInstallScriptLocator(Func<string, bool> fileExists, IReadOnlyList<string> candidateDirs)
    {
        _fileExists = fileExists;
        _candidateDirs = candidateDirs;
    }

    public string? Locate()
        => _candidateDirs
            .Select(dir => Path.Combine(dir, ScriptName))
            .FirstOrDefault(_fileExists);

    private static List<string> DefaultCandidateDirs()
    {
        var dirs = new List<string>();
        var overrideDir = Environment.GetEnvironmentVariable(DirEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            dirs.Add(overrideDir);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            dirs.Add(Path.Combine(localAppData, "PodBridge", "advanced-tier"));
        }

        dirs.Add(Path.Combine(AppContext.BaseDirectory, "advanced-tier"));
        return dirs;
    }
}

/// <summary>
/// Real launcher: ShellExecute with verb "runas" so exactly the launched tool is elevated (one
/// UAC prompt); the calling app is untouched. A declined prompt (ERROR_CANCELLED) is reported
/// as <see langword="false"/>, not thrown.
/// </summary>
internal sealed class ShellElevatedProcessLauncher : IElevatedProcessLauncher
{
    private const int ErrorCancelled = 1223; // user declined the UAC prompt.

    public bool Launch(ElevatedLaunchSpec spec)
    {
        var info = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            UseShellExecute = true, // required for the "runas" verb (elevation).
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(info);
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false; // opt-in: declining the prompt changes nothing.
        }
    }
}
