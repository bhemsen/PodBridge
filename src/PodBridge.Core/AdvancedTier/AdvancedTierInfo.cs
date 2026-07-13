namespace PodBridge.Core.AdvancedTier;

/// <summary>
/// Device-independent, honest user-facing copy for the OPTIONAL advanced tier's opt-in
/// enable flow (spec docs/specs/spec-advanced-driver-anc.md). Kept in Core (no UI
/// dependency) so the honesty invariants are covered by the constitution's Tier-1
/// device-independent test gate — see <c>AdvancedTierInfoTests</c>. The App renders these
/// strings verbatim in the "Enable advanced tier" warning; the same facts appear in
/// docs/user/advanced-tier.md.
/// <para>
/// Honesty gate: the copy states BOTH x64 load requirements — (1) enabling Windows
/// test-signing mode yourself and (2) trusting a self-signed test certificate — and their
/// combined machine-wide security trade-off, and makes NO claim of a Microsoft-signed /
/// production-attested driver.
/// </para>
/// </summary>
public static class AdvancedTierInfo
{
    /// <summary>Short menu/label heading for the opt-in affordance.</summary>
    public const string Title = "Enable the advanced tier";

    /// <summary>
    /// One-line summary of what the advanced tier is and that it is an opt-in add-on that
    /// installs a driver — never installed silently or by default.
    /// </summary>
    public const string Summary =
        "The advanced tier adds noise-control switching (Off / Noise Cancellation / "
        + "Transparency / Adaptive) on supported AirPods. It is an optional add-on that "
        + "installs a small kernel driver separately from PodBridge — never automatically.";

    /// <summary>
    /// The honest security explanation shown before the elevated install runs. States both
    /// machine-wide load requirements and the trade-off, and that PodBridge never runs
    /// <c>bcdedit</c> for the user. Contains no Microsoft-signed / production claim.
    /// </summary>
    public const string SecurityWarning =
        "Loading this driver on 64-bit Windows requires TWO machine-wide security changes, "
        + "and it is NOT a Microsoft-signed driver:\n\n"
        + "1. Test-signing mode — you must enable it yourself with "
        + "\"bcdedit /set testsigning on\" and reboot. PodBridge never runs bcdedit for you.\n"
        + "2. Trusting a self-signed test certificate — the installer imports it into your "
        + "machine's Trusted Root Certification Authorities and Trusted Publishers stores.\n\n"
        + "Together these lower your machine's driver-security bar until you undo them. Both "
        + "are opt-in and reversible, and every default (Tier-1) feature keeps working "
        + "without them. Continue to the elevated installer?";

    /// <summary>
    /// Shown after the elevated installer was started, reminding the user of the remaining
    /// manual test-signing step (PodBridge never performs it).
    /// </summary>
    public const string LaunchedFollowUp =
        "The advanced-tier installer was started — approve the Windows admin prompt. When it "
        + "finishes, enable test-signing yourself (\"bcdedit /set testsigning on\") and reboot. "
        + "PodBridge re-checks for the driver on the next launch.";

    /// <summary>
    /// Shown when the driver package is not present locally: the driver ships separately from
    /// the app, so the App points the user at the documentation to obtain and install it.
    /// </summary>
    public const string PackageMissingFollowUp =
        "The advanced-tier driver isn't on this PC. It ships separately from PodBridge as its "
        + "own download (never bundled in the app). Opening the advanced-tier guide, which links "
        + "to the driver release and explains how to install it.";
}
