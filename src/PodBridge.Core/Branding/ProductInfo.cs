namespace PodBridge.Core.Branding;

/// <summary>
/// Device-independent product-identity and legal strings surfaced by the About
/// window and any other user-facing branding. Kept in Core (no UI dependency) so
/// the constitution's branding/disclaimer invariants are covered by the Tier-1
/// device-independent test gate — see <c>ProductInfoTests</c>. The app version is
/// intentionally NOT here: it is read from the running assembly at runtime.
/// </summary>
public static class ProductInfo
{
    /// <summary>
    /// The coined product name. Deliberately contains neither "Apple" nor "AirPods"
    /// (constitution Don'ts + vision non-goals: third-party trademark guidelines
    /// forbid the marks in the product name, and no Apple logo is used).
    /// </summary>
    public const string Name = "PodBridge";

    /// <summary>
    /// The descriptive tagline. "for AirPods" is used descriptively only — to say
    /// which hardware the tool interoperates with — never as part of the name.
    /// </summary>
    public const string Descriptor = "for AirPods on Windows";

    /// <summary>One-line summary of what the product is.</summary>
    public const string Tagline = "An open-source AirPods companion for Windows.";

    /// <summary>
    /// The mandatory not-affiliated disclaimer (constitution Don'ts; mirrors the
    /// trademark notice in the repo-root NOTICE file). Must be shown verbatim in the
    /// About surface.
    /// </summary>
    public const string Disclaimer =
        "PodBridge is not affiliated with, authorized, sponsored, or endorsed by "
        + "Apple Inc. \"AirPods\" and \"Apple\" are trademarks of Apple Inc., used "
        + "here only descriptively to identify the hardware this software works "
        + "with. PodBridge uses no Apple logo.";

    /// <summary>SPDX license identifier; must match the tree's declared license.</summary>
    public const string LicenseId = "Apache-2.0";

    /// <summary>Human-readable license name paired with <see cref="LicenseId"/>.</summary>
    public const string LicenseName = "Apache License, Version 2.0";

    /// <summary>
    /// Honest audio/mic note (constitution honest-audio-surface principle). Never
    /// claims Apple-parity sound; states the AAC-vs-SBC ceiling and the A2DP-to-HFP
    /// microphone trade-off truthfully as a Bluetooth-Classic platform limit.
    /// </summary>
    public const string AudioNote =
        "Audio honesty: PodBridge never claims Apple-identical sound. On supported "
        + "hardware Windows plays media over AAC, the best codec available on "
        + "Windows, but not identical to Apple's. Using the AirPods microphone "
        + "forces a Bluetooth call profile (HFP) that drops playback to mono call "
        + "quality; this A2DP-to-HFP trade-off is a Bluetooth-Classic platform "
        + "limit, not a bug. PodBridge manages it, it does not solve it.";

    /// <summary>The project home page (local-only ethos: source, not a hosted app).</summary>
    public const string ProjectUrl = "https://github.com/bhemsen/PodBridge";

    /// <summary>Link to the in-repo user documentation (no hosted docs site).</summary>
    public const string DocsUrl = "https://github.com/bhemsen/PodBridge/tree/main/docs";

    /// <summary>
    /// Link to the OPTIONAL advanced-tier guide (noise-control driver: opt-in install,
    /// the two x64 load requirements + their security trade-off). The "Enable advanced
    /// tier" affordance opens this when the driver package isn't present locally.
    /// </summary>
    public const string AdvancedTierDocsUrl =
        "https://github.com/bhemsen/PodBridge/blob/main/docs/user/advanced-tier.md";
}
