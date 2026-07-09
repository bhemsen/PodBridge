using PodBridge.Core.Media;

namespace PodBridge.Core.Tests.Media;

/// <summary>
/// Device-independent stand-in for a real media transport (the GSMTC adapter). Its
/// <see cref="PlaybackState"/> is settable so a test can stage "playing" vs
/// "user-paused" media, and it counts pause/resume calls so the engine's decisions
/// can be asserted with no physical device / no media app (Tier-1 test gate).
/// Pause/resume update the reported state to mimic the real transport.
/// </summary>
internal sealed class FakeMediaController : IMediaController
{
    public PlaybackState PlaybackState { get; set; } = PlaybackState.None;

    public int PauseCallCount { get; private set; }

    public int ResumeCallCount { get; private set; }

    public PlaybackState GetPlaybackState() => PlaybackState;

    public void Pause()
    {
        PauseCallCount++;
        PlaybackState = PlaybackState.Paused;
    }

    public void Resume()
    {
        ResumeCallCount++;
        PlaybackState = PlaybackState.Playing;
    }
}
