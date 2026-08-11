using PodBridge.Core.AdvancedTier;
using Xunit;

namespace PodBridge.Core.Tests.AdvancedTier;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the honest advanced-tier opt-in
/// copy the App renders. They pin the honesty invariants of issue #45: the warning states
/// BOTH x64 load requirements — enabling test-signing mode (which the user does themselves;
/// PodBridge never runs <c>bcdedit</c>) AND trusting the self-signed test certificate (Trusted
/// Root CA / Trusted Publishers) — plus their machine-wide trade-off, and makes no claim of a
/// Microsoft-signed / production driver.
/// </summary>
public class AdvancedTierInfoTests
{
    [Fact]
    public void SecurityWarning_states_test_signing_mode_requirement()
    {
        Assert.Contains("test-signing", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bcdedit", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecurityWarning_says_PodBridge_never_runs_bcdedit_itself()
        => Assert.Contains(
            "never runs bcdedit", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void SecurityWarning_states_the_test_cert_trust_requirement_into_both_stores()
    {
        Assert.Contains("Trusted Root", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trusted Publishers", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecurityWarning_states_the_machine_wide_security_tradeoff()
        => Assert.Contains(
            "machine-wide", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);

    // The copy used to promise "TWO" changes while silently requiring a third: test-signing
    // mode cannot be enabled while Secure Boot is on. Understating the cost of a machine-wide
    // security downgrade is exactly what the honest-surface rule forbids.
    [Fact]
    public void SecurityWarning_states_the_Secure_Boot_precondition()
    {
        Assert.Contains("Secure Boot", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THREE", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "TWO machine-wide", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
    }

    // With HVCI enforcing, a test-signed driver is refused regardless of the three steps —
    // so without this warning a user can lower two protections and still get nothing.
    [Fact]
    public void SecurityWarning_warns_that_Memory_Integrity_blocks_the_load_entirely()
    {
        Assert.Contains(
            "Memory Integrity", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "refuses to load", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);
    }

    // BitLocker plus a Secure Boot change is a real way to lock yourself out of your own disk.
    [Fact]
    public void SecurityWarning_warns_about_the_BitLocker_recovery_prompt()
        => Assert.Contains(
            "recovery-key", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void SecurityWarning_makes_no_Microsoft_signed_claim()
        => Assert.Contains(
            "NOT a Microsoft-signed", AdvancedTierInfo.SecurityWarning, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Summary_makes_clear_the_tier_is_optional_and_not_automatic()
    {
        Assert.Contains("optional", AdvancedTierInfo.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never automatically", AdvancedTierInfo.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
