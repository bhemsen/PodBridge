using PodBridge.Core.AdvancedTier;
using PodBridge.Windows;
using PodBridge.Windows.Interop;
using PodBridge.Windows.Tests.Interop;
using Xunit;

namespace PodBridge.Windows.Tests;

/// <summary>
/// Device-independent tests for <see cref="AdvancedTierInstaller"/> at the Win32 install seam
/// (fake locator + fake launcher). They pin the opt-in decision logic: locate the
/// shipped-separately script and launch it ELEVATED (a UAC prompt) for the right action,
/// report <c>PackageMissing</c> when the driver package is absent (→ App opens the docs),
/// report <c>Cancelled</c> when the user declines the prompt, and NEVER launch the app itself.
/// The real elevated install + test-signing + hardware load is the documented human smoke test
/// (spec docs/specs/spec-advanced-driver-anc.md; CI has no hardware).
/// </summary>
public sealed class AdvancedTierInstallerTests
{
    private const string ScriptPath = @"C:\pkg\install-advanced-tier.ps1";

    [Fact]
    public void Install_launches_powershell_elevated_with_the_install_action()
    {
        var launcher = new FakeElevatedProcessLauncher();
        var installer = new AdvancedTierInstaller(
            new FakeInstallScriptLocator { ScriptPath = ScriptPath }, launcher);

        var result = installer.Install();

        Assert.Equal(AdvancedTierActionResult.Launched, result);
        Assert.Equal(1, launcher.LaunchCount);
        var spec = launcher.LastSpec!.Value;
        // Launches PowerShell running the script — never the app's own executable, so the
        // asInvoker app is never elevated (constitution: opt-in invasiveness).
        Assert.Equal("powershell.exe", spec.FileName);
        Assert.Contains($"-File \"{ScriptPath}\"", spec.Arguments, StringComparison.Ordinal);
        Assert.Contains("-Action install", spec.Arguments, StringComparison.Ordinal);
        Assert.Contains("-ExecutionPolicy Bypass", spec.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_launches_powershell_with_the_uninstall_action()
    {
        var launcher = new FakeElevatedProcessLauncher();
        var installer = new AdvancedTierInstaller(
            new FakeInstallScriptLocator { ScriptPath = ScriptPath }, launcher);

        var result = installer.Uninstall();

        Assert.Equal(AdvancedTierActionResult.Launched, result);
        Assert.Contains("-Action uninstall", launcher.LastSpec!.Value.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_when_the_package_is_absent_reports_PackageMissing_and_launches_nothing()
    {
        var launcher = new FakeElevatedProcessLauncher();
        var installer = new AdvancedTierInstaller(
            new FakeInstallScriptLocator { ScriptPath = null }, launcher);

        var result = installer.Install();

        Assert.Equal(AdvancedTierActionResult.PackageMissing, result);
        Assert.Equal(0, launcher.LaunchCount); // never elevate when there is nothing to install
    }

    [Fact]
    public void Install_when_the_user_declines_the_prompt_reports_Cancelled()
    {
        var launcher = new FakeElevatedProcessLauncher { Result = false }; // UAC declined
        var installer = new AdvancedTierInstaller(
            new FakeInstallScriptLocator { ScriptPath = ScriptPath }, launcher);

        var result = installer.Install();

        Assert.Equal(AdvancedTierActionResult.Cancelled, result);
    }

    [Fact]
    public void ScriptLocator_returns_the_first_candidate_folder_that_has_the_script()
    {
        var present = Path.Combine(@"C:\second", DefaultInstallScriptLocator.ScriptName);
        var locator = new DefaultInstallScriptLocator(
            path => string.Equals(path, present, StringComparison.OrdinalIgnoreCase),
            [@"C:\first", @"C:\second"]);

        Assert.Equal(present, locator.Locate());
    }

    [Fact]
    public void ScriptLocator_returns_null_when_no_candidate_has_the_script()
    {
        var locator = new DefaultInstallScriptLocator(_ => false, [@"C:\first", @"C:\second"]);

        Assert.Null(locator.Locate());
    }
}
