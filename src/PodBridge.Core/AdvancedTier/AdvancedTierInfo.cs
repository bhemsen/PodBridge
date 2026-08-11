namespace PodBridge.Core.AdvancedTier;

/// <summary>
/// Device-independent, honest user-facing copy for the OPTIONAL advanced tier's opt-in
/// enable flow (spec docs/specs/spec-advanced-driver-anc.md). Kept in Core (no UI
/// dependency) so the honesty invariants are covered by the constitution's Tier-1
/// device-independent test gate — see <c>AdvancedTierInfoTests</c>. The App renders these
/// strings verbatim in the "Enable advanced tier" warning; the same facts appear in
/// docs/user/advanced-tier.md.
/// <para>
/// Honesty gate: the copy states ALL THREE x64 load requirements — (1) turning Secure Boot
/// off, (2) enabling Windows test-signing mode yourself and (3) trusting a self-signed test
/// certificate — plus the Memory-Integrity (HVCI) blocker that refuses the driver regardless,
/// their combined machine-wide security trade-off, and makes NO claim of a Microsoft-signed /
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
    /// The honest security explanation shown before the elevated install runs. States all
    /// THREE machine-wide load requirements — Secure Boot off, test-signing mode on, and the
    /// self-signed test certificate trusted — plus the Memory-Integrity (HVCI) blocker that
    /// stops the driver loading regardless, the combined trade-off, and that PodBridge never
    /// runs <c>bcdedit</c> for the user. Contains no Microsoft-signed / production claim.
    /// <para>
    /// Secure Boot and HVCI were added after a user asked whether this driver could have
    /// bricked a PC: the copy promised "TWO" changes while silently requiring a third (Secure
    /// Boot off is a precondition of test-signing), and never mentioned that on a machine with
    /// Memory Integrity enforcing the driver cannot load at all — so a user could lower two
    /// machine-wide protections and still get nothing.
    /// </para>
    /// </summary>
    public const string SecurityWarning =
        "Loading this driver on 64-bit Windows requires THREE machine-wide security changes, "
        + "and it is NOT a Microsoft-signed driver:\n\n"
        + "1. Secure Boot must be OFF — test-signing mode cannot be enabled while it is on. "
        + "You turn it off in your PC's UEFI/BIOS setup; PodBridge cannot and does not change "
        + "it. If your disk is BitLocker-encrypted, changing Secure Boot can trigger a "
        + "recovery-key prompt on the next boot — have your key ready.\n"
        + "2. Test-signing mode — you must enable it yourself with "
        + "\"bcdedit /set testsigning on\" and reboot. PodBridge never runs bcdedit for you.\n"
        + "3. Trusting a self-signed test certificate — the installer imports it into your "
        + "machine's Trusted Root Certification Authorities and Trusted Publishers stores.\n\n"
        + "Check Memory Integrity too (Windows Security > Device security > Core isolation). "
        + "While it is on, Windows refuses to load a test-signed driver even after all three "
        + "steps above — you would lower your security for nothing.\n\n"
        + "Together these lower your machine's driver-security bar until you undo them. All "
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
