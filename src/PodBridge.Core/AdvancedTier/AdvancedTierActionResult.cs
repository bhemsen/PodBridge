namespace PodBridge.Core.AdvancedTier;

/// <summary>
/// Outcome of a user-triggered advanced-tier install / uninstall request routed
/// through <see cref="IAdvancedTierInstaller"/>. Device-independent so the App can
/// decide what honest follow-up to show without referencing any OS type
/// (spec docs/specs/spec-advanced-driver-anc.md).
/// </summary>
public enum AdvancedTierActionResult
{
    /// <summary>
    /// The separate, elevated installer/uninstaller was started (the user was shown
    /// the Windows admin prompt). The app itself was NOT elevated — it stays
    /// <c>asInvoker</c>; only the launched install step is elevated.
    /// </summary>
    Launched,

    /// <summary>
    /// The user declined the elevation prompt, so nothing was changed. Not an error —
    /// the advanced tier is strictly opt-in.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The advanced-tier driver package/script is not present on this PC (it ships
    /// separately from the app MSIX and is never bundled). The App should guide the
    /// user to the documentation to obtain and install it.
    /// </summary>
    PackageMissing,
}
