namespace PodBridge.Core.AdvancedTier;

/// <summary>
/// OS-boundary abstraction for the OPTIONAL advanced tier's separate, user-triggered,
/// ELEVATED driver-install step (spec docs/specs/spec-advanced-driver-anc.md). The App
/// resolves this interface and invokes it only on an explicit user action; the concrete
/// Windows adapter locates the shipped-separately install script and launches it elevated
/// (a single UAC prompt), performing the driver install (<c>pnputil</c>) and the test-cert
/// trust in that one step.
/// <para>
/// Contract (constitution: opt-in invasiveness, <c>asInvoker</c> app): implementations
/// MUST NOT elevate the calling app, MUST NOT install anything silently, and MUST NEVER
/// enable test-signing mode (<c>bcdedit</c>) — that stays a documented manual user step.
/// Core stays OS-free: this is an interface only, implemented in <c>PodBridge.Windows</c>.
/// </para>
/// </summary>
public interface IAdvancedTierInstaller
{
    /// <summary>
    /// Launches the elevated advanced-tier install step (driver <c>pnputil</c> install +
    /// self-signed test-cert trust into Trusted Root CA / Trusted Publishers). Returns
    /// <see cref="AdvancedTierActionResult.PackageMissing"/> when the driver package is not
    /// present locally (the App then points the user at the documentation), and
    /// <see cref="AdvancedTierActionResult.Cancelled"/> when the user declines the UAC prompt.
    /// </summary>
    AdvancedTierActionResult Install();

    /// <summary>
    /// Launches the elevated advanced-tier uninstall step (driver removal + un-trusting the
    /// test cert). Same result semantics as <see cref="Install"/>.
    /// </summary>
    AdvancedTierActionResult Uninstall();
}
