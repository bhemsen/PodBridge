using PodBridge.Core.Media;
using Windows.Foundation;
using Windows.Media.Control;

namespace PodBridge.Windows;

/// <summary>
/// WinRT implementation of <see cref="IMediaController"/> over the Global System
/// Media Transport Controls (GSMTC) session manager. Drives pause/resume of
/// whatever media Windows deems the current session — the same target as the
/// hardware media keys. Tier 1: pure WinRT projection, no driver, no admin
/// (<c>asInvoker</c>). API choices per <c>docs/research/media-control.md</c> (#17).
/// </summary>
/// <remarks>
/// GSMTC's <see cref="GlobalSystemMediaTransportControlsSessionManager.RequestAsync"/>
/// is asynchronous while the <see cref="IMediaController"/> surface is a
/// pull-based, synchronous read plus fire-and-forget commands, so the manager is
/// acquired once in the background at construction. Until it is ready — or if
/// acquisition fails (research: GSMTC needs an <b>interactive user session</b> and
/// throws <c>0x80070424</c> in a non-interactive service/SYSTEM context) — every
/// operation degrades gracefully to a no-op / <see cref="PlaybackState.None"/>.
/// The current session is read fresh on every call (the authoritative read per the
/// research), so no <c>CurrentSessionChanged</c> subscription is needed. Phase 2
/// targets the current session only.
/// </remarks>
public sealed class WindowsMediaController : IMediaController, IDisposable
{
    private readonly Lock _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _initStarted;

    /// <summary>
    /// Begins acquiring the GSMTC session manager in the background. The controller
    /// is usable immediately; calls made before acquisition completes safely no-op.
    /// </summary>
    public WindowsMediaController() => _ = InitializeAsync();

    /// <inheritdoc />
    public PlaybackState GetPlaybackState()
    {
        var session = CurrentSession();
        if (session is null)
        {
            return PlaybackState.None;
        }

        try
        {
            return Map(session.GetPlaybackInfo().PlaybackStatus);
        }
        catch (Exception)
        {
            // A stale/closing session can fault on read; treat as no live state.
            return PlaybackState.None;
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        var session = CurrentSession();
        if (session is not null)
        {
            _ = TryControlAsync(session.TryPauseAsync());
        }
    }

    /// <inheritdoc />
    public void Resume()
    {
        var session = CurrentSession();
        if (session is not null)
        {
            _ = TryControlAsync(session.TryPlayAsync());
        }
    }

    /// <summary>Drops the cached manager reference.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _manager = null;
        }

        GC.SuppressFinalize(this);
    }

    private async Task InitializeAsync()
    {
        lock (_gate)
        {
            if (_initStarted)
            {
                return;
            }

            _initStarted = true;
        }

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync().AsTask().ConfigureAwait(false);
            lock (_gate)
            {
                _manager = manager;
            }
        }
        catch (Exception)
        {
            // Graceful degradation: no manager (e.g. non-interactive session) →
            // every operation stays a safe no-op / PlaybackState.None.
        }
    }

    private GlobalSystemMediaTransportControlsSession? CurrentSession()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_gate)
        {
            manager = _manager;
        }

        if (manager is null)
        {
            return null; // not initialized yet, or acquisition failed
        }

        try
        {
            return manager.GetCurrentSession(); // null when nothing is playing
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task TryControlAsync(IAsyncOperation<bool> operation)
    {
        try
        {
            await operation.AsTask().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Fire-and-forget: an unsupported/expired control simply does nothing.
        }
    }

    private static PlaybackState Map(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        _ => PlaybackState.Other,
    };
}
