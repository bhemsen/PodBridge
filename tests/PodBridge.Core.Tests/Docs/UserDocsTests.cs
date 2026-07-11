using PodBridge.Core.Audio;
using PodBridge.Core.Branding;
using Xunit;

namespace PodBridge.Core.Tests.Docs;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the user documentation
/// authored for issue #37 (README quickstart + <c>docs/user/</c> guide). They pin the
/// docs to the <b>shipped</b> Core strings — the honest codec/mic tray lines
/// (<see cref="AudioGuidanceEngine"/>), the single-device degrade warning
/// (<see cref="MicPolicyEngine"/>), and the branding/disclaimer constants
/// (<see cref="ProductInfo"/>) — so the docs cannot silently drift from what the app
/// actually shows, and assert the honest-audio invariant: no doc claims Apple-parity
/// sound (constitution "Honest audio surface").
/// </summary>
public class UserDocsTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly string Readme =
        File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

    private static readonly string UserGuide =
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "user", "README.md"));

    // The mic-profile mode labels shown in the tray "Microphone mode" submenu. These
    // live in the App (TrayIcon.cs), not Core, so they are pinned here as literals the
    // docs must match verbatim; if a label is renamed the docs test forces a docs update.
    private static readonly string[] MicModeLabels =
        ["HiFi-lock", "Auto-switch", "Call-mode", "AirPods mic (Call-mode)"];

    // Apple-parity CLAIMS the honest-audio principle forbids anywhere in the docs. The
    // docs phrase honesty positively ("never pretends to reproduce Apple's own sound"),
    // so none of these claim phrases appear even in a negated form.
    private static readonly string[] ForbiddenAppleParityClaims =
        [
            "apple-identical", "apple parity", "apple-parity", "identical to apple",
            "as good as apple", "same sound as apple", "matches apple", "indistinguishable",
        ];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "PodBridge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Docs_CarryBrandingAndDisclaimer()
    {
        foreach (var doc in new[] { Readme, UserGuide })
        {
            Assert.Contains(ProductInfo.Name, doc, StringComparison.Ordinal);
            Assert.Contains(ProductInfo.Descriptor, doc, StringComparison.Ordinal);
            Assert.Contains("not affiliated", doc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ProductInfo.LicenseId, doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UserGuide_QuotesTheShippedCodecAndMicTrayLines()
    {
        Assert.Contains(AudioGuidanceEngine.CodecLineAac, UserGuide, StringComparison.Ordinal);
        Assert.Contains(AudioGuidanceEngine.CodecLineSbc, UserGuide, StringComparison.Ordinal);
        Assert.Contains(AudioGuidanceEngine.MicLineHighQuality, UserGuide, StringComparison.Ordinal);
        Assert.Contains(AudioGuidanceEngine.MicLineCallMode, UserGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void UserGuide_DocumentsAllThreeMicModesAndTheDegradeWarning()
    {
        foreach (var label in MicModeLabels)
        {
            Assert.Contains(label, UserGuide, StringComparison.Ordinal);
        }

        Assert.Contains(
            MicPolicyEngine.NoAlternateMicWarningText, UserGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void UserGuide_DocumentsDownloadAndRunPlusVerificationAndAutoStartDefaultOff()
    {
        // release-1.0 pivot: download-and-run (no MSIX/Store/winget), with an honest
        // verify-your-download section (checksum + attestation + SmartScreen reality).
        Assert.Contains("win-x64", UserGuide, StringComparison.Ordinal);
        Assert.Contains("win-arm64", UserGuide, StringComparison.Ordinal);
        Assert.Contains("certutil -hashfile", UserGuide, StringComparison.Ordinal);
        Assert.Contains("gh attestation verify", UserGuide, StringComparison.Ordinal);
        Assert.Contains("Unknown publisher", UserGuide, StringComparison.Ordinal);
        Assert.DoesNotContain("msstore", UserGuide, StringComparison.Ordinal);
        Assert.DoesNotContain("winget install", UserGuide, StringComparison.Ordinal);
        Assert.DoesNotContain("Add-AppxPackage", UserGuide, StringComparison.Ordinal);

        // Auto-start (issue #35) is opt-in, default OFF.
        Assert.Contains("off by default", UserGuide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Docs_NeverClaimAppleParitySound()
    {
        foreach (var doc in new[] { Readme, UserGuide })
        {
            foreach (var claim in ForbiddenAppleParityClaims)
            {
                Assert.DoesNotContain(claim, doc, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
