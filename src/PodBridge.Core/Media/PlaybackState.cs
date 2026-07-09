namespace PodBridge.Core.Media;

/// <summary>
/// OS-free view of the current media session's playback state, as surfaced by
/// <see cref="IMediaController"/>. Maps the Windows GSMTC
/// <c>GlobalSystemMediaTransportControlsSessionPlaybackStatus</c> down to the
/// distinctions the auto play/pause engine needs, so Core never references a WinRT
/// type. The engine only pauses when it observes <see cref="Playing"/>.
/// </summary>
public enum PlaybackState
{
    /// <summary>No controllable media session exists, or media control is unavailable.</summary>
    None = 0,

    /// <summary>A session exists but is neither playing nor paused (opening, changing, stopped, closed).</summary>
    Other = 1,

    /// <summary>Media is actively playing.</summary>
    Playing = 2,

    /// <summary>Media is paused.</summary>
    Paused = 3,
}
