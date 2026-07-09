namespace PodBridge.Core.Media;

/// <summary>
/// OS-free control surface over "whatever media is currently playing on the host".
/// Tier 1: no driver, no admin. Implemented on Windows by <c>WindowsMediaController</c>
/// via the media-session manager (GSMTC). The adapter is deliberately primitive —
/// it exposes only a playback-state read plus pause/resume; the "pause only when
/// playing" and "resume only if PodBridge paused" decisions live in
/// <see cref="AutoPlayPauseEngine"/> so they are unit-testable with a fake
/// (constitution: Core is platform-neutral and OS-free).
/// </summary>
public interface IMediaController
{
    /// <summary>
    /// Reads a fresh snapshot of the current media session's playback state, or
    /// <see cref="PlaybackState.None"/> when there is no controllable session or
    /// media control is unavailable. Never throws.
    /// </summary>
    PlaybackState GetPlaybackState();

    /// <summary>
    /// Requests the current media session pause. A no-op when there is no session.
    /// Fire-and-forget: it does not wait for the request to be acknowledged and
    /// never throws.
    /// </summary>
    void Pause();

    /// <summary>
    /// Requests the current media session resume playing. A no-op when there is no
    /// session. Fire-and-forget: it does not wait for the request to be
    /// acknowledged and never throws. Callers must only invoke this to resume media
    /// they themselves paused (the engine enforces the paused-by-us rule).
    /// </summary>
    void Resume();
}
