using PodBridge.Core.AdvancedTier;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows adapter for the OPTIONAL advanced tier's separate, user-triggered, ELEVATED
/// install step (issue #45; spec docs/specs/spec-advanced-driver-anc.md). It locates the
/// shipped-separately install script (<c>install-advanced-tier.ps1</c>) and launches it
/// elevated via PowerShell — a single UAC prompt on the launched step only. That one step
/// installs the driver (<c>pnputil /add-driver … /install</c>) and trusts the self-signed
/// test certificate (Trusted Root CA / Trusted Publishers); it never enables test-signing
/// mode (<c>bcdedit</c>) — that stays a documented manual user action.
/// <para>
/// Constitution: the calling app stays <c>asInvoker</c> — this adapter elevates only the
/// launched PowerShell + script, never PodBridge itself, and does nothing silently. When the
/// driver package is absent (it is never bundled in the app MSIX) it reports
/// <see cref="AdvancedTierActionResult.PackageMissing"/> so the App guides the user to the docs.
/// The real elevated install + test-signing + hardware load is the documented human smoke test;
/// the decision logic here is device-independently unit-tested via fakes at the Win32 seam.
/// </para>
/// </summary>
public sealed class AdvancedTierInstaller : IAdvancedTierInstaller
{
    private const string PowerShellExe = "powershell.exe";
    private const string InstallAction = "install";
    private const string UninstallAction = "uninstall";

    private readonly IInstallScriptLocator _locator;
    private readonly IElevatedProcessLauncher _launcher;

    /// <summary>Production constructor: probes the default folders and elevates via ShellExecute.</summary>
    public AdvancedTierInstaller()
        : this(new DefaultInstallScriptLocator(), new ShellElevatedProcessLauncher())
    {
    }

    // Test seam: PodBridge.Windows.Tests substitutes fakes so the locate/launch decision is
    // exercised with no filesystem, no elevation, and no driver.
    internal AdvancedTierInstaller(IInstallScriptLocator locator, IElevatedProcessLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(launcher);
        _locator = locator;
        _launcher = launcher;
    }

    /// <inheritdoc />
    public AdvancedTierActionResult Install() => Run(InstallAction);

    /// <inheritdoc />
    public AdvancedTierActionResult Uninstall() => Run(UninstallAction);

    private AdvancedTierActionResult Run(string action)
    {
        var scriptPath = _locator.Locate();
        if (scriptPath is null)
        {
            return AdvancedTierActionResult.PackageMissing; // ships separately -> App opens docs
        }

        // Launch PowerShell (never the app) running the script for the given action. -File plus
        // an explicit ExecutionPolicy Bypass so a restrictive machine policy can't block the
        // opt-in the user just approved.
        var arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Action {action}";
        var launched = _launcher.Launch(new ElevatedLaunchSpec(PowerShellExe, arguments));
        return launched ? AdvancedTierActionResult.Launched : AdvancedTierActionResult.Cancelled;
    }
}
