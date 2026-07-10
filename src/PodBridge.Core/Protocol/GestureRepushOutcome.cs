namespace PodBridge.Core.Protocol;

/// <summary>
/// The result of a <see cref="GestureRepushController"/> re-push attempt — the reaction to
/// a Tier-2 (re)connect. All outcomes are non-fatal: a failure to re-apply never crashes
/// the app or hammers the channel (spec docs/specs/spec-gesture-remap.md write-confirm rule).
/// </summary>
public enum GestureRepushOutcome
{
    /// <summary>
    /// The Tier-2 transport was unavailable (driver absent — the Tier-1 default): no frame
    /// was sent (graceful degradation).
    /// </summary>
    Unavailable,

    /// <summary>
    /// No configuration is stored yet (the user has never assigned a gesture): nothing was
    /// sent, so an unconfigured device is left untouched.
    /// </summary>
    NoConfiguration,

    /// <summary>The AirPods echoed the re-pushed configuration back: it is confirmed applied.</summary>
    Confirmed,

    /// <summary>
    /// No matching echo arrived after the initial send and a single retry: a non-fatal
    /// "couldn't apply" outcome. There is no further retry (no retry storm); the next
    /// (re)connect will try again.
    /// </summary>
    CouldNotApply,
}
