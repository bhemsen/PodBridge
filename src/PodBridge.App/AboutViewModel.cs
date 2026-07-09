using System.IO;
using System.Reflection;
using PodBridge.Core.Branding;

namespace PodBridge.App;

/// <summary>
/// Read-only view model for the <see cref="AboutWindow"/>. Composes the
/// device-independent branding/legal strings from <see cref="ProductInfo"/> (Core)
/// with the running app version and the third-party-notices text shipped in the
/// payload's THIRD-PARTY-NOTICES.md. Holds no mutable state; the version and notices
/// are resolved once at construction via <see cref="Create"/>.
/// </summary>
public sealed class AboutViewModel
{
    private const string NoticesFileName = "THIRD-PARTY-NOTICES.md";

    private const string FallbackNotices =
        "Full third-party notices ship in THIRD-PARTY-NOTICES.md at the project "
        + "root. PodBridge bundles H.NotifyIcon (MIT), Microsoft.Extensions.* (MIT), "
        + "and the Windows SDK .NET projections / CsWinRT (MIT). PodBridge itself is "
        + "licensed under Apache-2.0.";

    public AboutViewModel(string version, string thirdPartyNotices)
    {
        Version = version;
        VersionLine = $"Version {version}";
        ThirdPartyNotices = thirdPartyNotices;
    }

    // These are get-only auto-properties (backed by fields) rather than
    // expression-bodied forwarders so they remain bindable from XAML and do not trip
    // CA1822; their values are the device-independent Core branding constants.

    /// <summary>Coined product name (no "Apple"/"AirPods" in it).</summary>
    public string ProductName { get; } = ProductInfo.Name;

    /// <summary>The "for AirPods on Windows" descriptor (descriptive use only).</summary>
    public string Descriptor { get; } = ProductInfo.Descriptor;

    /// <summary>One-line product summary.</summary>
    public string Tagline { get; } = ProductInfo.Tagline;

    /// <summary>Mandatory not-affiliated / trademark disclaimer.</summary>
    public string Disclaimer { get; } = ProductInfo.Disclaimer;

    /// <summary>Honest audio/mic note (never claims Apple-parity sound).</summary>
    public string AudioNote { get; } = ProductInfo.AudioNote;

    /// <summary>License line rendered from the Core license constants.</summary>
    public string LicenseLine { get; }
        = $"Licensed under the {ProductInfo.LicenseName} ({ProductInfo.LicenseId}).";

    /// <summary>Link to the in-repo user documentation.</summary>
    public Uri DocsUri { get; } = new(ProductInfo.DocsUrl);

    /// <summary>Link to the project home page.</summary>
    public Uri ProjectUri { get; } = new(ProductInfo.ProjectUrl);

    /// <summary>Running app version, e.g. "Version 1.0.0".</summary>
    public string VersionLine { get; }

    /// <summary>Third-party attribution/notices text surfaced in the window.</summary>
    public string ThirdPartyNotices { get; }

    /// <summary>The resolved app version string (no leading "Version " label).</summary>
    public string Version { get; }

    /// <summary>
    /// Builds the view model for the running app: the informational version from the
    /// app assembly and the third-party notices from the shipped file (with a safe
    /// fallback when the file is absent, e.g. an unpackaged run).
    /// </summary>
    public static AboutViewModel Create()
        => new(ResolveVersion(), LoadThirdPartyNotices());

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Drop any build-metadata / source-revision suffix (e.g. "1.0.0+abc123").
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : version.ToString(3);
    }

    private static string LoadThirdPartyNotices()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, NoticesFileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: fall back to the built-in summary below.
        }

        return FallbackNotices;
    }
}
