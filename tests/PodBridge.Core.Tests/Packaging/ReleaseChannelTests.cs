using System.Xml.Linq;
using Xunit;

namespace PodBridge.Core.Tests.Packaging;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the Phase-5 release
/// channel authored for issue #36: the self-signed GitHub-Releases MSIX fallback
/// (the <c>Package &amp; Release (MSIX)</c> workflow) and the Microsoft Store /
/// <c>msstore</c> distribution prep. These assert the #36 acceptance items as
/// repo invariants without any packaged runtime, real device, or network:
/// the release workflow triggers on a version tag and attaches the signed MSIX
/// plus its matching trust certificate; the self-signed signer stays coupled to
/// the manifest <c>Publisher</c>; the Store identity/product-ID are documented
/// Partner-Center-derived placeholders (not invented or self-signed values); and
/// the one-time certificate-trust step is documented.
/// </summary>
public class ReleaseChannelTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string ReadRepoFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray()));

    private static readonly string Workflow =
        ReadRepoFile(".github", "workflows", "packaging.yml");

    private static readonly string PackagingReadme =
        ReadRepoFile("packaging", "README.md");

    private static readonly string MsstoreEntry =
        ReadRepoFile("packaging", "msstore", "msstore-entry.yaml");

    private static readonly string ReleaseNotes =
        ReadRepoFile("packaging", "release-notes.template.md");

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
    public void ReleaseWorkflow_TriggersOnSemverTag()
    {
        // A tag push is what promotes a build to a GitHub Release.
        Assert.Contains("tags:", Workflow, StringComparison.Ordinal);
        Assert.Contains("'v*'", Workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_AttachesSignedMsixAndTrustCertToTheRelease()
    {
        // Both assets must be attached: the signed MSIX and the .cer users need
        // to trust the self-signed signer before it will install.
        Assert.Contains("steps.find.outputs.msix", Workflow, StringComparison.Ordinal);
        Assert.Contains("steps.cert.outputs.cer", Workflow, StringComparison.Ordinal);
        Assert.Contains("PodBridge-SelfSigned.cer", Workflow, StringComparison.Ordinal);

        // The release step must be gated on a tag ref, so branch/PR builds only
        // publish a workflow artifact and never create a Release.
        Assert.Contains("startsWith(github.ref, 'refs/tags/v')", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SelfSignedSigner_StaysCoupledToTheManifestPublisher()
    {
        // The self-signed cert Subject MUST equal the fallback manifest Publisher
        // or signtool signing fails (research source 6). Guard against drift: the
        // workflow must reference the exact Publisher string from the manifest.
        var manifest = XDocument.Load(Path.Combine(
            RepoRoot, "packaging", "PodBridge.Package", "Package.appxmanifest"));
        var publisher = manifest.Descendants()
            .First(e => e.Name.LocalName == "Identity")
            .Attribute("Publisher")!.Value;

        Assert.Equal("CN=PodBridge (Self-Signed CI)", publisher);
        Assert.Contains(publisher, Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MsstoreEntry_IsAPlaceholderStoreIdOnTheMsstoreSource()
    {
        // The msstore PackageIdentifier is the Partner-Center-derived Store
        // product ID; it does not exist yet, so it must stay an explicit
        // placeholder (never an invented ID) and target the msstore source.
        Assert.Contains("STORE_PRODUCT_ID_TBD", MsstoreEntry, StringComparison.Ordinal);
        Assert.Contains("source: msstore", MsstoreEntry, StringComparison.Ordinal);
        Assert.Contains("-s msstore", MsstoreEntry, StringComparison.Ordinal);

        // Not verified here: the no-admin install is deferred to #38 (post-cert).
        Assert.Contains("verified: false", MsstoreEntry, StringComparison.Ordinal);
    }

    [Fact]
    public void StoreAssociationTemplate_UsesPartnerCenterPlaceholders_NotSelfSignedOrAppleName()
    {
        var template = XDocument.Load(Path.Combine(
            RepoRoot, "packaging", "msstore", "store-association.template.xml"));

        string Value(string local) =>
            template.Descendants().First(e => e.Name.LocalName == local).Value;

        // Store identity is Partner-Center-derived: publisher/name are unfilled
        // placeholders, and explicitly NOT the throwaway self-signed identity.
        var publisher = Value("Publisher");
        Assert.StartsWith("CN=", publisher, StringComparison.Ordinal);
        Assert.Contains("TBD", publisher, StringComparison.Ordinal);
        Assert.NotEqual("CN=PodBridge (Self-Signed CI)", publisher);
        Assert.Contains("TBD", Value("MainPackageIdentityName"), StringComparison.Ordinal);

        // Branding invariant on the Store listing name (constitution): the
        // reserved name carries no "Apple"/"AirPods".
        var reservedName = Value("ReservedName");
        Assert.Equal("PodBridge", reservedName);
        Assert.DoesNotContain("apple", reservedName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("airpods", reservedName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagingDocs_DocumentTheOneTimeCertTrustStep_AndTheStoreCommand()
    {
        // The self-signed manual fallback requires a one-time, admin cert-trust
        // step into the machine TrustedPeople store; the primary no-admin path is
        // the Store via winget msstore. Both must stay documented.
        Assert.Contains("Cert:\\LocalMachine\\TrustedPeople", PackagingReadme, StringComparison.Ordinal);
        Assert.Contains("Import-Certificate", PackagingReadme, StringComparison.Ordinal);
        Assert.Contains("-s msstore", PackagingReadme, StringComparison.Ordinal);

        // The app never elevates at run time.
        Assert.Contains("asInvoker", PackagingReadme, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseNotes_AreHonestAboutAudioAndAffiliation()
    {
        // Constitution: honest audio surface (never claim Apple-parity) + the
        // not-affiliated disclaimer. The template carries the __TAG__ token the
        // workflow substitutes at release time, and it points at the self-signed
        // fallback's one-time trust step.
        Assert.Contains("__TAG__", ReleaseNotes, StringComparison.Ordinal);
        Assert.Contains("Apple-identical", ReleaseNotes, StringComparison.Ordinal);
        Assert.Contains("Not affiliated", ReleaseNotes, StringComparison.Ordinal);
        Assert.Contains("Cert:\\LocalMachine\\TrustedPeople", ReleaseNotes, StringComparison.Ordinal);
    }
}
