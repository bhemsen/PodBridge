using PodBridge.Core.Branding;
using Xunit;

namespace PodBridge.Core.Tests.Docs;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the advanced-tier user guide
/// authored for issue #45. They pin the honesty gate: the doc states ALL THREE x64 load
/// requirements (Secure Boot off, test-signing mode via <c>bcdedit</c>, AND trusting the
/// self-signed test cert in Trusted Root CA / Trusted Publishers), the Memory-Integrity
/// blocker that refuses the driver regardless, their trade-off, documents the opt-in
/// <c>pnputil</c> install, makes NO Microsoft-signed / production claim, and records the
/// attestation path (EV cert + Partner Center) as deferred / out of scope — so the shipped
/// doc cannot silently drift from the honesty the spec requires.
/// </summary>
public class AdvancedTierDocsTests
{
    private static readonly string Guide = File.ReadAllText(
        Path.Combine(FindRepoRoot(), "docs", "user", "advanced-tier.md"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PodBridge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Guide_StatesAllThreeX64LoadRequirements()
    {
        // (1) Secure Boot off — the precondition the guide used to omit entirely, which made
        // it promise a two-step cost for what is really three (and the costliest one at that).
        Assert.Contains("Secure Boot", Guide, StringComparison.OrdinalIgnoreCase);
        // (2) test-signing mode, stated as the user's own manual bcdedit step.
        Assert.Contains("bcdedit /set testsigning on", Guide, StringComparison.Ordinal);
        // (3) trusting the self-signed test cert in BOTH machine stores.
        Assert.Contains("Trusted Root", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trusted Publishers", Guide, StringComparison.OrdinalIgnoreCase);
    }

    // With HVCI enforcing, all three steps above still leave the driver unloadable — so a
    // guide that omits it invites the user to weaken the machine for nothing.
    [Fact]
    public void Guide_NamesMemoryIntegrityAsABlocker()
    {
        Assert.Contains("Memory Integrity", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core isolation", Guide, StringComparison.OrdinalIgnoreCase);
    }

    // Changing Secure Boot on a BitLocker disk can lock the user out of their own machine.
    [Fact]
    public void Guide_WarnsAboutTheBitLockerRecoveryPrompt()
    {
        Assert.Contains("BitLocker", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery key", Guide, StringComparison.OrdinalIgnoreCase);
    }

    // The guide must tell a user how to get out, not just how to get in.
    [Fact]
    public void Guide_DocumentsARecoveryPath()
    {
        Assert.Contains("Safe Mode", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pnputil /delete-driver", Guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guide_StatesTheSecurityTradeoffAndThatPodBridgeNeverRunsBcdedit()
    {
        Assert.Contains("machine-wide", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never", Guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guide_DocumentsTheOptInPnputilInstall()
    {
        Assert.Contains("pnputil", Guide, StringComparison.Ordinal);
        Assert.Contains("install-advanced-tier.ps1", Guide, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_MakesNoMicrosoftSignedClaim()
        => Assert.Contains(
            "makes no claim of a Microsoft-signed", Guide, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Guide_RecordsAttestationAsDeferredOutOfScope()
    {
        Assert.Contains("EV code-signing certificate", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Partner Center", Guide, StringComparison.Ordinal);
        Assert.Contains("out of scope", Guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Guide_CarriesBrandingAndDisclaimer()
    {
        Assert.Contains(ProductInfo.Name, Guide, StringComparison.Ordinal);
        Assert.Contains("not affiliated", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ProductInfo.LicenseId, Guide, StringComparison.Ordinal);
    }
}
