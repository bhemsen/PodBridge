namespace PodBridge.Core.Diagnostics;

/// <summary>
/// The honest, static Tier-2 driver signing/test-mode facts for the diagnostics snapshot
/// (constitution: "diagnostics reports the driver's signing/test-mode status truthfully" —
/// carries forward the Phase-6 signing honesty in <see cref="AdvancedTier.AdvancedTierInfo"/>).
/// <para>
/// These are fixed, documented facts about how the optional driver is built and loaded
/// (spec docs/specs/spec-advanced-driver-anc.md) — <b>not</b> a live per-machine
/// test-signing probe. Phase 8 reopens no signing decision: it states the same two facts
/// <see cref="AdvancedTier.AdvancedTierInfo.SecurityWarning"/> already tells the user before
/// they opt in, keyed only on whether the driver is currently loaded
/// (<see cref="Protocol.IAapTransport.IsAvailable"/>).
/// </para>
/// </summary>
public static class DriverSigningStatus
{
    /// <summary>Shown when the optional driver is absent — the Tier-1 default.</summary>
    public const string NoDriverInstalled =
        "No optional driver installed (Tier-1 default — no admin, no elevation).";

    /// <summary>
    /// Shown when the driver is loaded. States plainly that it is self-signed and
    /// test-mode-only, never a Microsoft-signed / production-attested driver.
    /// </summary>
    public const string TestSignedDriverPresent =
        "Optional driver present: a self-signed, test-mode-only driver "
        + "(never Microsoft-signed) — requires Windows test-signing mode, which the user "
        + "enabled themselves.";
}
