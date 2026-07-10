using PodBridge.Core.Branding;
using Xunit;

namespace PodBridge.Core.Tests.Docs;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the advanced-tier user guide
/// authored for issue #45. They pin the honesty gate: the doc states BOTH x64 load
/// requirements (test-signing mode via <c>bcdedit</c> AND trusting the self-signed test cert
/// in Trusted Root CA / Trusted Publishers) and their trade-off, documents the opt-in
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
    public void Guide_StatesBothX64LoadRequirements()
    {
        // (1) test-signing mode, stated as the user's own manual bcdedit step.
        Assert.Contains("bcdedit /set testsigning on", Guide, StringComparison.Ordinal);
        // (2) trusting the self-signed test cert in BOTH machine stores.
        Assert.Contains("Trusted Root", Guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trusted Publishers", Guide, StringComparison.OrdinalIgnoreCase);
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
