using System.Xml.Linq;
using Xunit;

namespace PodBridge.Core.Tests.Packaging;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the MSIX packaging
/// authored in <c>packaging/PodBridge.Package</c>. These assert issue-#34
/// acceptance as repo invariants: the coined product name carries no
/// "Apple"/"AirPods" as the name, capabilities stay minimal, the packaged app
/// runs as an unelevated full-trust desktop app (asInvoker), and no kernel
/// driver is part of the package (app-only).
/// </summary>
public class PackagingManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly XDocument Manifest =
        XDocument.Load(Path.Combine(
            RepoRoot, "packaging", "PodBridge.Package", "Package.appxmanifest"));

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

    private static IEnumerable<XElement> ByLocalName(string localName) =>
        Manifest.Descendants().Where(e => e.Name.LocalName == localName);

    [Fact]
    public void DisplayName_IsTheCoinedNameWithNoAppleOrAirPodsInTheName()
    {
        var displayName = ByLocalName("DisplayName").First().Value;

        Assert.Equal("PodBridge", displayName);
        Assert.DoesNotContain("apple", displayName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("airpods", displayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capabilities_AreMinimal_RunFullTrustAndBluetoothOnly()
    {
        var capabilities = ByLocalName("Capabilities").Single();
        var names = capabilities.Elements()
            .Select(e => (string?)e.Attribute("Name"))
            .Where(n => n is not null)
            .ToHashSet();

        // Exactly the minimal set the app needs: full-trust desktop app + BLE.
        Assert.Equal(new HashSet<string?> { "runFullTrust", "bluetooth" }, names);
    }

    [Fact]
    public void Application_IsAnUnelevatedFullTrustDesktopApp()
    {
        var app = ByLocalName("Application").Single();

        Assert.Equal("PodBridge.App.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)app.Attribute("EntryPoint"));
    }

    [Fact]
    public void AppManifest_KeepsAsInvoker_NoElevationAtRunTime()
    {
        var appManifest = XDocument.Load(Path.Combine(
            RepoRoot, "src", "PodBridge.App", "app.manifest"));
        var level = appManifest.Descendants()
            .First(e => e.Name.LocalName == "requestedExecutionLevel")
            .Attribute("level")?.Value;

        Assert.Equal("asInvoker", level);
    }

    [Fact]
    public void Package_ContainsNoKernelDriver()
    {
        // No driver payload referenced in the manifest ...
        var manifestText = File.ReadAllText(Path.Combine(
            RepoRoot, "packaging", "PodBridge.Package", "Package.appxmanifest"));
        Assert.DoesNotContain(".sys", manifestText, StringComparison.OrdinalIgnoreCase);

        // ... and the packaging project wraps PodBridge.App only (no driver project).
        var wapproj = File.ReadAllText(Path.Combine(
            RepoRoot, "packaging", "PodBridge.Package", "PodBridge.Package.wapproj"));
        var references = XDocument.Parse(wapproj).Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, r => Assert.EndsWith("PodBridge.App.csproj", r));
        Assert.DoesNotContain(".sys", wapproj, StringComparison.OrdinalIgnoreCase);
    }
}
