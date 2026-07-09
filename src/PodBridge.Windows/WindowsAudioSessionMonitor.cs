using System.Runtime.InteropServices;
using PodBridge.Core.Audio;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// Windows implementation of <see cref="IAudioSessionMonitor"/> — detects a
/// Communications-role capture (microphone) session opening/closing driver-free
/// and admin-free (<c>asInvoker</c>), so the mic-profile policy can Auto-switch.
/// </summary>
/// <remarks>
/// <para>Per docs/research/mic-profile-policy-comms-detection.md (#26): a session's
/// Communications <i>category</i> is not observer-readable, so mic engagement is
/// derived from <b>device role + session state</b>. The monitor activates
/// <see cref="IAudioSessionManager2WithNotifications"/> on the <c>eCommunications</c>
/// <b>capture</b> endpoint and treats the mic as engaged when any non-system-sounds
/// session is <see cref="AudioSessionState.Active"/> — the authoritative signal for
/// the mic case (HFP trigger #1). It complements this with duck notifications
/// (<see cref="IAudioVolumeDuckNotification"/>), the OS's own "communication stream
/// opened/closed" signal, which additionally covers the Communications-<i>render</i>
/// case (HFP trigger #2) the capture scan cannot see.</para>
/// <para><b>Event-primary, reconciled.</b> <c>OnSessionCreated</c> and duck/unduck
/// callbacks are treated as triggers that prompt a re-enumeration; the capture side
/// is re-derived authoritatively on every tick, and the render-side duck signal is
/// tracked from the OS's own <c>countCommunicationSessions</c> refcount rather than a
/// self-maintained id set, so a silently dropped unduck cannot pin it on. A coarse
/// safety re-scan closes the documented "notifications silently stop" gap. A single
/// debounced boolean (active capture session OR an open communication session) drives the Core-facing
/// events: <see cref="CommunicationsCaptureStarted"/> on its <c>false→true</c> edge,
/// <see cref="CommunicationsCaptureStopped"/> on <c>true→false</c> — so an overlap
/// (one call ending as another continues) never flaps.</para>
/// <para><b>Never opens a stream.</b> Only <c>Activate</c> / <c>GetSessionEnumerator</c>
/// / <c>GetState</c> / notifications are used — never <c>IAudioClient::Initialize</c>
/// or <c>Start</c> on the capture endpoint, which would itself force HFP. COM is
/// initialised MTA on a dedicated background thread; the manager and notification
/// callback objects are kept alive for the monitor's lifetime and released on stop.
/// Any COM fault degrades to inert, never a crash.</para>
/// </remarks>
public sealed class WindowsAudioSessionMonitor : IAudioSessionMonitor, IDisposable
{
    // Coarse fallback re-scan: bounds detection latency if a notification is missed
    // (research #26 §3 — never rely on notifications alone) without busy-polling.
    private static readonly TimeSpan SafetyRescanInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();

    // The OS's own live count of open communication sessions on the eCommunications
    // endpoint (research #26 §2, the "natural refcount for open/close bookkeeping").
    // A duck REPLACES it with the OS-reported countCommunicationSessions (never
    // accumulates), an unduck best-effort decrements it, and DropManager/Stop reset it.
    // Using the OS count instead of a self-maintained id set means a single silently
    // dropped unduck (research #26 §3) can no longer pin the render-side signal on
    // until Stop(): it is healed by the next duck's authoritative count or an endpoint
    // change. > 0 means a communication stream (render or capture) is open.
    private int _communicationSessionCount;

    private Thread? _comThread;
    private AutoResetEvent? _wake;
    private volatile bool _stopRequested;
    private bool _started;

    // The following are touched only on the COM thread (no lock required).
    private IAudioSessionManager2WithNotifications? _manager;
    private SessionCreatedCallback? _sessionNotification;
    private DuckCallback? _duckNotification;
    private bool _lastEngaged;

    /// <inheritdoc />
    public event EventHandler? CommunicationsCaptureStarted;

    /// <inheritdoc />
    public event EventHandler? CommunicationsCaptureStopped;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _stopRequested = false;
            _wake = new AutoResetEvent(false);
            _comThread = new Thread(ComThreadMain)
            {
                IsBackground = true,
                Name = "PodBridge.AudioSessionMonitor",
            };
            _comThread.SetApartmentState(ApartmentState.MTA);
            _comThread.Start();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _stopRequested = true;
            thread = _comThread;
            _comThread = null;
            _wake?.Set();
        }

        // The COM thread unregisters notifications and releases COM in its finally
        // before it exits (see ComThreadMain), so no callback can run afterwards.
        thread?.Join(TimeSpan.FromSeconds(5));

        lock (_gate)
        {
            _wake?.Dispose();
            _wake = null;
            _communicationSessionCount = 0;

            // Reset the debounced edge state (safe now the COM thread has exited) so a
            // Start→engaged→Stop→Start cycle re-detects from a clean "not engaged"
            // baseline and never emits a spurious Stopped on the next start.
            _lastEngaged = false;
        }
    }

    /// <summary>Stops the monitor and releases its wait handle.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    // Owns all COM work on one MTA thread: acquire the comms-capture manager, then
    // reconcile on every wake (a notification trigger) and on each safety tick.
    private void ComThreadMain()
    {
        var wake = _wake!;
        try
        {
            EnsureManager();
            Reconcile();
            while (!_stopRequested)
            {
                wake.WaitOne(SafetyRescanInterval);
                if (_stopRequested)
                {
                    break;
                }

                EnsureManager();
                Reconcile();
            }
        }
        catch (Exception)
        {
            // A COM/threading fault must never crash the host; the monitor goes
            // inert (constitution: graceful degradation).
        }
        finally
        {
            DropManager();
        }
    }

    private void EnsureManager()
    {
        _manager ??= TryCreateManager();
    }

    private IAudioSessionManager2WithNotifications? TryCreateManager()
    {
        object? enumeratorObject = null;
        try
        {
            var comType = Type.GetTypeFromCLSID(NativeMethods.MMDeviceEnumeratorClsid);
            enumeratorObject = comType is null ? null : Activator.CreateInstance(comType);
            if (enumeratorObject is not IMMDeviceEnumeratorWithDefault enumerator)
            {
                return null;
            }

            return CreateManagerForCommsCapture(enumerator);
        }
        catch (COMException)
        {
            // No comms-capture endpoint present yet, or activation failed — the next
            // safety tick retries (e.g. after a capture device appears).
            return null;
        }
        finally
        {
            if (enumeratorObject is not null)
            {
                Marshal.ReleaseComObject(enumeratorObject);
            }
        }
    }

    private IAudioSessionManager2WithNotifications? CreateManagerForCommsCapture(
        IMMDeviceEnumeratorWithDefault enumerator)
    {
        enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Communications, out var endpoint);
        if (endpoint is null)
        {
            return null;
        }

        try
        {
            var iid = typeof(IAudioSessionManager2WithNotifications).GUID;
            endpoint.Activate(ref iid, NativeMethods.ClsCtxAll, IntPtr.Zero, out var raw);
            var manager = (IAudioSessionManager2WithNotifications)raw;
            RegisterNotifications(manager);
            return manager;
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    private void RegisterNotifications(IAudioSessionManager2WithNotifications manager)
    {
        _sessionNotification = new SessionCreatedCallback(RequestRescan);
        _duckNotification = new DuckCallback(OnDuck, OnUnduck);

        // Registration results are best-effort: a failure just means we lean on the
        // safety re-scan, and must never throw the monitor down.
        _ = manager.RegisterSessionNotification(_sessionNotification);
        _ = manager.RegisterDuckNotification(null, _duckNotification);
    }

    private void DropManager()
    {
        // The comms endpoint is being released (default-device change / device removed):
        // reset the render-side duck signal so stale state from the old endpoint cannot
        // carry over and pin "comms open" against the freshly-acquired endpoint.
        lock (_gate)
        {
            _communicationSessionCount = 0;
        }

        var manager = _manager;
        _manager = null;
        if (manager is null)
        {
            return;
        }

        try
        {
            if (_sessionNotification is not null)
            {
                _ = manager.UnregisterSessionNotification(_sessionNotification);
            }

            if (_duckNotification is not null)
            {
                _ = manager.UnregisterDuckNotification(_duckNotification);
            }
        }
        catch (COMException)
        {
            // The endpoint is already gone; nothing left to unregister.
        }
        finally
        {
            _sessionNotification = null;
            _duckNotification = null;
            Marshal.ReleaseComObject(manager);
        }
    }

    // Recompute the debounced "mic engaged" boolean from a fresh capture-session
    // scan plus the self-maintained duck set, and raise Core events only on edges.
    private void Reconcile()
    {
        int commCount;
        lock (_gate)
        {
            commCount = _communicationSessionCount;
        }

        var captureActive = false;
        if (_manager is not null)
        {
            try
            {
                captureActive = HasActiveCaptureSession(_manager);
            }
            catch (COMException)
            {
                // The endpoint became invalid (device removed / default changed);
                // drop it so the next tick re-acquires the current comms endpoint.
                DropManager();
            }
        }

        RaiseIfChanged(captureActive || commCount > 0);
    }

    private void RaiseIfChanged(bool engaged)
    {
        if (engaged == _lastEngaged)
        {
            return;
        }

        _lastEngaged = engaged;
        if (engaged)
        {
            CommunicationsCaptureStarted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            CommunicationsCaptureStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    // Read-only: enumerates sessions and reads state. It never opens a stream on the
    // capture endpoint (no IAudioClient::Initialize/Start), so observing it cannot
    // itself force the HFP switch (research #26, critical constraint).
    private static bool HasActiveCaptureSession(IAudioSessionManager2WithNotifications manager)
    {
        manager.GetSessionEnumerator(out var sessions);
        try
        {
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var control);
                try
                {
                    if (IsMicEngagingSession(control))
                    {
                        return true;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(sessions);
        }

        return false;
    }

    // A running (Active) session that is not the system-sounds session — the exact
    // shape of an app holding the microphone open (research #26 §2 primary signal).
    private static bool IsMicEngagingSession(IAudioSessionControl control)
    {
        control.GetState(out var state);
        if (state != AudioSessionState.Active)
        {
            return false;
        }

        // IsSystemSoundsSession returns S_OK (0) when it IS the system-sounds session.
        // If the QueryInterface for IAudioSessionControl2 is unsupported, default to
        // counting the session (safer to over-detect the mic than to miss it).
        return control is not IAudioSessionControl2 control2 || control2.IsSystemSoundsSession() != 0;
    }

    // countCommunicationSessions is the OS's authoritative live count of open
    // communication sessions. Replace (never accumulate) so the render-side signal
    // self-heals from any earlier missed unduck.
    private void OnDuck(uint countCommunicationSessions)
    {
        lock (_gate)
        {
            _communicationSessionCount = (int)countCommunicationSessions;
        }

        RequestRescan();
    }

    // Unduck carries no count; best-effort decrement floored at 0. Any residual drift
    // is corrected by the next duck's authoritative count or an endpoint change.
    private void OnUnduck()
    {
        lock (_gate)
        {
            if (_communicationSessionCount > 0)
            {
                _communicationSessionCount--;
            }
        }

        RequestRescan();
    }

    // Wakes the COM thread to reconcile. Guarded by the gate so a late callback after
    // Stop() (once _wake is nulled) is a harmless no-op rather than a disposed-handle throw.
    private void RequestRescan()
    {
        lock (_gate)
        {
            _wake?.Set();
        }
    }

    // Callback objects are kept alive via the _sessionNotification / _duckNotification
    // fields for as long as they are registered (research #26 §5).
    private sealed class SessionCreatedCallback : IAudioSessionNotification
    {
        private readonly Action _onSessionCreated;

        internal SessionCreatedCallback(Action onSessionCreated) =>
            _onSessionCreated = onSessionCreated;

        // A session is created Inactive; its later transition into Active is what
        // forces HFP, so this is used only as a prompt to re-reconcile. It must not
        // block (the callback runs on an OS background thread, research #26 §4).
        public void OnSessionCreated(IAudioSessionControl newSession) => _onSessionCreated();
    }

    private sealed class DuckCallback : IAudioVolumeDuckNotification
    {
        private readonly Action<uint> _onDuck;
        private readonly Action _onUnduck;

        internal DuckCallback(Action<uint> onDuck, Action onUnduck)
        {
            _onDuck = onDuck;
            _onUnduck = onUnduck;
        }

        // The session id is not needed: the OS-authoritative countCommunicationSessions
        // is what drives the render-side signal (research #26 §2), and the boolean
        // "any comms session open" does not depend on which session it is.
        public void OnVolumeDuckNotification(string? sessionID, uint countCommunicationSessions) =>
            _onDuck(countCommunicationSessions);

        public void OnVolumeUnduckNotification(string? sessionID) => _onUnduck();
    }
}
