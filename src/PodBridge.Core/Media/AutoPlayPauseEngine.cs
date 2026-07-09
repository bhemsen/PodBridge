using PodBridge.Core.Bluetooth;
using PodBridge.Core.Models;

namespace PodBridge.Core.Media;

/// <summary>
/// Drives automatic media pause/resume from AirPods in-ear/out-of-ear transitions.
/// Subscribes to the connection-gated <see cref="IDeviceStateProvider"/> and keys
/// off <see cref="DeviceState.AnyInEar"/> (at least one bud in an ear):
/// <list type="bullet">
/// <item>Out-of-ear (<see cref="DeviceState.AnyInEar"/> true→false): pause the
/// current media <b>only if it is playing</b>, and record "paused-by-us".</item>
/// <item>In-ear (<see cref="DeviceState.AnyInEar"/> false→true): resume <b>only if
/// PodBridge recorded the pause</b> — never resume media the user paused.</item>
/// </list>
/// <para>
/// Connection gate: no pause/resume ever fires unless <see cref="IConnectionMonitor"/>
/// reports <see cref="ConnectionStatus.Connected"/>. The provider already reports
/// <see cref="DeviceState.Unknown"/> while disconnected, but the engine re-checks the
/// gate directly so a transition can never leak through (spec: gate play/pause on the
/// Phase-1 connection state). Targets the current media session only in Phase 2.
/// </para>
/// </summary>
public sealed class AutoPlayPauseEngine : IDisposable
{
    private readonly IDeviceStateProvider _stateProvider;
    private readonly IMediaController _mediaController;
    private readonly IConnectionMonitor _connectionMonitor;
    private readonly Lock _gate = new();

    private bool _anyInEar;
    private bool _pausedByUs;

    /// <summary>
    /// Wires the engine to its sources and seeds the in-ear baseline from the
    /// provider's current state. It only subscribes; starting the underlying
    /// scanner/monitor is the composition root's responsibility.
    /// </summary>
    /// <param name="stateProvider">Connection-gated in-ear/battery telemetry source.</param>
    /// <param name="mediaController">Pause/resume control over the current media session.</param>
    /// <param name="connectionMonitor">Phase-1 connection state used as the hard play/pause gate.</param>
    public AutoPlayPauseEngine(
        IDeviceStateProvider stateProvider,
        IMediaController mediaController,
        IConnectionMonitor connectionMonitor)
    {
        ArgumentNullException.ThrowIfNull(stateProvider);
        ArgumentNullException.ThrowIfNull(mediaController);
        ArgumentNullException.ThrowIfNull(connectionMonitor);

        _stateProvider = stateProvider;
        _mediaController = mediaController;
        _connectionMonitor = connectionMonitor;
        _anyInEar = stateProvider.Current.AnyInEar;
        _stateProvider.StateChanged += OnStateChanged;
    }

    /// <summary>Unsubscribes from the state provider.</summary>
    public void Dispose()
    {
        _stateProvider.StateChanged -= OnStateChanged;
        GC.SuppressFinalize(this);
    }

    private void OnStateChanged(object? sender, DeviceState state)
    {
        // Hard gate: never drive media unless the user's AirPods are connected,
        // even if an in-ear/out-of-ear transition arrives.
        if (_connectionMonitor.CurrentStatus != ConnectionStatus.Connected)
        {
            return;
        }

        lock (_gate)
        {
            var nowInEar = state.AnyInEar;
            if (nowInEar == _anyInEar)
            {
                return; // no in/out transition
            }

            if (nowInEar)
            {
                ResumeIfWePausedLocked();
            }
            else
            {
                PauseIfPlayingLocked();
            }

            _anyInEar = nowInEar;
        }
    }

    // Bud removed: pause only while media is actually playing; remember we did it.
    private void PauseIfPlayingLocked()
    {
        if (_mediaController.GetPlaybackState() == PlaybackState.Playing)
        {
            _mediaController.Pause();
            _pausedByUs = true;
        }
    }

    // Bud re-inserted: resume only the media PodBridge itself paused.
    private void ResumeIfWePausedLocked()
    {
        if (_pausedByUs)
        {
            _mediaController.Resume();
            _pausedByUs = false;
        }
    }
}
