using System.Linq;

namespace PodBridge.Core.Audio;

/// <summary>
/// The platform-neutral microphone-profile policy engine. Given the current
/// <see cref="MicPolicyMode"/>, the Call-mode toggle, and comms-capture-session
/// open/close signals from <see cref="IAudioSessionMonitor"/>, it computes and
/// applies the correct endpoint-role assignment through <see cref="IAudioPolicy"/>:
/// <list type="bullet">
/// <item><b>HiFi-lock</b> — AirPods hold the media (Console + Multimedia) render
/// role; the fallback holds the Communications render + capture role.</item>
/// <item><b>Auto-switch</b> — as HiFi-lock while idle; promotes the AirPods to the
/// Communications role on a comms-capture-session open and <b>restores the
/// HiFi-lock assignment</b> on close.</item>
/// <item><b>Call-mode</b> — a manual toggle swaps the Communications role to/from
/// the AirPods on demand, independent of any live session.</item>
/// </list>
/// <para>
/// Every role decision hinges purely on each endpoint's
/// <see cref="AudioEndpoint.IsAirPods"/> flag — the engine never identifies an
/// endpoint itself (constitution: Core is OS-free). The fallback is the current
/// non-AirPods default-communications device, else the first non-AirPods device.
/// </para>
/// <para>
/// <b>Single-device degrade:</b> when no non-AirPods fallback exists, HiFi-lock and
/// Auto-switch collapse to Call-mode behaviour (the comms role follows the manual
/// toggle, default off) and <see cref="NoAlternateMicWarning"/> is raised — the
/// engine never silently forces the AirPods into HFP (spec prior decision).
/// </para>
/// </summary>
public sealed class MicPolicyEngine : IDisposable
{
    /// <summary>Honest warning surfaced when there is no non-AirPods fallback mic.</summary>
    public const string NoAlternateMicWarningText =
        "No alternate mic — AirPods mic requires HFP/mono.";

    /// <summary>
    /// Honest warning surfaced when the policy wants the AirPods mic (a comms promotion
    /// was requested) but Windows exposes no active AirPods capture endpoint, so the mic
    /// could not actually be engaged — never presented as set when it is not.
    /// </summary>
    public const string AirPodsMicUnavailableText =
        "AirPods mic unavailable — enable Hands-Free/Headset for the AirPods in Windows " +
        "Bluetooth settings, or re-pair.";

    private readonly IAudioPolicy _audioPolicy;
    private readonly IAudioSessionMonitor _sessionMonitor;
    private readonly IAudioEndpointChangeMonitor _endpointChangeMonitor;
    private readonly ICommsProfileEngager _commsProfileEngager;
    private readonly Lock _gate = new();

    // The user's endpoint-role assignment captured BEFORE the engine first mutated it —
    // the "prior" state the restore path returns audio to on an apply failure or on
    // graceful shutdown (hardening: PodBridge leaves no rerouted audio behind).
    private readonly Dictionary<(AudioRole Role, AudioEndpointDirection Direction), string> _baseline = [];

    private MicPolicyMode _mode = MicPolicyMode.HiFiLock;
    private bool _callModeActive;
    private bool _commsCaptureOpen;
    private bool _noAlternateMicWarning;
    private bool _airPodsMicUnavailable;

    /// <summary>
    /// Wires the engine to its audio-policy and session-monitor sources and applies
    /// the initial assignment for the default <see cref="MicPolicyMode.HiFiLock"/>
    /// mode. It only subscribes; starting the underlying monitor is the composition
    /// root's responsibility.
    /// </summary>
    /// <param name="audioPolicy">Endpoint enumeration + per-role default setter.</param>
    /// <param name="sessionMonitor">Comms-capture-session open/close source.</param>
    /// <param name="endpointChangeMonitor">Device-topology change source; each change
    /// triggers a <see cref="Refresh"/> so the degrade warning tracks the fallback mic
    /// appearing or disappearing live.</param>
    /// <param name="commsProfileEngager">Forces the AirPods HFP link up (via a silent
    /// Communications-category render keep-alive) while they hold the comms role, so the
    /// AirPods capture endpoint comes live; released when the AirPods are demoted.</param>
    public MicPolicyEngine(
        IAudioPolicy audioPolicy,
        IAudioSessionMonitor sessionMonitor,
        IAudioEndpointChangeMonitor endpointChangeMonitor,
        ICommsProfileEngager commsProfileEngager)
    {
        ArgumentNullException.ThrowIfNull(audioPolicy);
        ArgumentNullException.ThrowIfNull(sessionMonitor);
        ArgumentNullException.ThrowIfNull(endpointChangeMonitor);
        ArgumentNullException.ThrowIfNull(commsProfileEngager);

        _audioPolicy = audioPolicy;
        _sessionMonitor = sessionMonitor;
        _endpointChangeMonitor = endpointChangeMonitor;
        _commsProfileEngager = commsProfileEngager;
        _sessionMonitor.CommunicationsCaptureStarted += OnCommsCaptureStarted;
        _sessionMonitor.CommunicationsCaptureStopped += OnCommsCaptureStopped;
        _endpointChangeMonitor.EndpointsChanged += OnEndpointsChanged;
        CaptureBaseline();         // snapshot the user's prior routing before we touch it
        Mutate(static () => { });  // establish the default HiFi-lock assignment
    }

    /// <summary>Raised when the no-alternate-mic degrade warning turns on or off.</summary>
    public event EventHandler<bool>? NoAlternateMicWarningChanged;

    /// <summary>Raised when the AirPods-mic-unavailable warning turns on or off.</summary>
    public event EventHandler<bool>? AirPodsMicUnavailableChanged;

    /// <summary>The currently-selected policy mode (default <see cref="MicPolicyMode.HiFiLock"/>).</summary>
    public MicPolicyMode CurrentMode
    {
        get { lock (_gate) { return _mode; } }
    }

    /// <summary>Whether the Call-mode toggle is currently on (AirPods hold comms).</summary>
    public bool CallModeActive
    {
        get { lock (_gate) { return _callModeActive; } }
    }

    /// <summary>
    /// <c>true</c> when there is no non-AirPods fallback for the comms role, so the
    /// policy has degraded to Call-mode behaviour and the honest warning applies.
    /// </summary>
    public bool NoAlternateMicWarning
    {
        get { lock (_gate) { return _noAlternateMicWarning; } }
    }

    /// <summary>
    /// <c>true</c> when a comms promotion to the AirPods was requested but Windows exposes
    /// no active AirPods capture endpoint, so the AirPods mic could not be engaged. The
    /// engine forces the HFP link up (see <see cref="ICommsProfileEngager"/>); this stays
    /// on until the capture endpoint appears and is assigned (then it self-clears), so the
    /// mic is never presented as set when it is not.
    /// </summary>
    public bool AirPodsMicUnavailable
    {
        get { lock (_gate) { return _airPodsMicUnavailable; } }
    }

    /// <summary>Selects the active <paramref name="mode"/> and re-applies the policy.</summary>
    public void SetMode(MicPolicyMode mode) => Mutate(() => _mode = mode);

    /// <summary>Sets the Call-mode toggle and re-applies the policy.</summary>
    public void SetCallModeActive(bool active) => Mutate(() => _callModeActive = active);

    /// <summary>
    /// Re-evaluates and re-applies the policy against the current endpoint list — call
    /// after a device-topology change (a device plugged in or removed).
    /// </summary>
    public void Refresh() => Mutate(static () => { });

    /// <summary>
    /// Restores the endpoint-role assignment captured before the engine's first apply —
    /// the user's prior audio routing. Call it on graceful shutdown so PodBridge leaves
    /// no rerouted default devices behind; the apply path also calls it internally to roll
    /// back a half-applied assignment when an endpoint-set fails (hardening: never crash,
    /// never leave a broken state). Endpoints that no longer exist are skipped.
    /// </summary>
    public void Restore()
    {
        lock (_gate)
        {
            RestoreBaselineLocked(_audioPolicy.GetEndpoints());
        }
    }

    /// <summary>Unsubscribes from the session and endpoint-change monitors.</summary>
    public void Dispose()
    {
        _sessionMonitor.CommunicationsCaptureStarted -= OnCommsCaptureStarted;
        _sessionMonitor.CommunicationsCaptureStopped -= OnCommsCaptureStopped;
        _endpointChangeMonitor.EndpointsChanged -= OnEndpointsChanged;
        GC.SuppressFinalize(this);
    }

    private void OnCommsCaptureStarted(object? sender, EventArgs e)
        => Mutate(() => _commsCaptureOpen = true);

    private void OnCommsCaptureStopped(object? sender, EventArgs e)
        => Mutate(() => _commsCaptureOpen = false);

    // A device was added/removed or the default changed: re-evaluate the assignment so
    // the degrade warning tracks the fallback mic appearing or disappearing live.
    private void OnEndpointsChanged(object? sender, EventArgs e) => Refresh();

    // Applies a state change under the gate, re-applies the policy, then raises the
    // warning-changed events OUTSIDE the lock to avoid handler reentrancy deadlocks.
    private void Mutate(Action change)
    {
        bool raiseNoAlt, noAltValue;
        bool raiseUnavailable, unavailableValue;
        lock (_gate)
        {
            change();
            var beforeNoAlt = _noAlternateMicWarning;
            var beforeUnavailable = _airPodsMicUnavailable;
            ApplyLocked();
            raiseNoAlt = beforeNoAlt != _noAlternateMicWarning;
            noAltValue = _noAlternateMicWarning;
            raiseUnavailable = beforeUnavailable != _airPodsMicUnavailable;
            unavailableValue = _airPodsMicUnavailable;
        }

        if (raiseNoAlt)
        {
            NoAlternateMicWarningChanged?.Invoke(this, noAltValue);
        }

        if (raiseUnavailable)
        {
            AirPodsMicUnavailableChanged?.Invoke(this, unavailableValue);
        }
    }

    // Recomputes the endpoint-role assignment for the current state. Caller holds _gate.
    private void ApplyLocked()
    {
        var endpoints = _audioPolicy.GetEndpoints();
        var airPodsRender = FindAirPods(endpoints, AudioEndpointDirection.Render);
        var airPodsCapture = FindAirPods(endpoints, AudioEndpointDirection.Capture);
        var fallbackRender = FindFallback(endpoints, AudioEndpointDirection.Render);
        var fallbackCapture = FindFallback(endpoints, AudioEndpointDirection.Capture);

        var degraded = fallbackRender is null || fallbackCapture is null;
        _noAlternateMicWarning = degraded;
        var promote = ShouldPromoteToAirPods(degraded);

        try
        {
            // AirPods stay the media (Console + Multimedia) render default when present.
            AssignMediaRender(airPodsRender);

            if (promote)
            {
                // Force the AirPods HFP/SCO link up so its capture endpoint comes live: a
                // routing-role set alone never wakes HFP (see ICommsProfileEngager). When
                // the capture endpoint appears a topology change re-enters ApplyLocked and
                // the now-present capture is assigned below, clearing the warning.
                EngageComms(airPodsRender);
                AssignComms(airPodsRender, airPodsCapture);
            }
            else
            {
                // Not promoting: drop the keep-alive so Windows can release the HFP link.
                _commsProfileEngager.Release();
                if (!degraded)
                {
                    AssignComms(fallbackRender, fallbackCapture);
                }

                // Degraded and not promoted: comms is left untouched — no silent HFP.
            }

            // Honest surface (candidate D): a promotion was requested but there is no
            // active AirPods capture endpoint to set, so the mic could NOT be engaged —
            // warn instead of silently presenting it as set. Self-clears once the capture
            // endpoint comes live (topology change → re-apply → AssignComms sets it).
            _airPodsMicUnavailable = promote && airPodsCapture is null;
        }
        catch (Exception)
        {
            // A mid-apply endpoint-set failure (an undocumented IPolicyConfig HRESULT
            // surfaced as an exception, or a device vanishing between enumerate and set)
            // must never crash the tray or leave a half-applied assignment: roll back to
            // the user's prior routing (constitution: graceful degradation).
            RestoreBaselineLocked(endpoints);
            _commsProfileEngager.Release();
            _airPodsMicUnavailable = false;
        }
    }

    // Holds the Communications-category render keep-alive on the AirPods render endpoint
    // to force HFP up. Nothing to engage without an AirPods render endpoint.
    private void EngageComms(AudioEndpoint? airPodsRender)
    {
        if (airPodsRender is not null)
        {
            _commsProfileEngager.Engage(airPodsRender.Id);
        }
    }

    // Reads the user's current default for each role/direction the engine will mutate,
    // BEFORE the first apply changes them, so Restore can return audio to the prior state.
    private void CaptureBaseline()
    {
        TryCaptureBaseline(AudioRole.Console, AudioEndpointDirection.Render);
        TryCaptureBaseline(AudioRole.Multimedia, AudioEndpointDirection.Render);
        TryCaptureBaseline(AudioRole.Communications, AudioEndpointDirection.Render);
        TryCaptureBaseline(AudioRole.Communications, AudioEndpointDirection.Capture);
    }

    private void TryCaptureBaseline(AudioRole role, AudioEndpointDirection direction)
    {
        var id = _audioPolicy.GetDefaultEndpoint(role, direction);
        if (id is not null)
        {
            _baseline[(role, direction)] = id;
        }
    }

    // Re-applies the captured prior assignment. Caller holds _gate. Only endpoints still
    // present are re-assigned, so a removed device never faults the restore.
    private void RestoreBaselineLocked(IReadOnlyList<AudioEndpoint> available)
    {
        foreach (var (key, id) in _baseline)
        {
            if (available.Any(e => e.Id == id && e.Direction == key.Direction))
            {
                TrySetDefault(id, key.Role);
            }
        }
    }

    // A soft, never-throwing endpoint-set used by the restore path so rolling back can
    // never itself crash the tray if a further set fails.
    private void TrySetDefault(string endpointId, AudioRole role)
    {
        try
        {
            _audioPolicy.SetDefaultEndpoint(endpointId, role);
        }
        catch (Exception)
        {
            // Best-effort restore: a failing set is swallowed (graceful degradation).
        }
    }

    // Whether the AirPods should currently hold the communications role.
    private bool ShouldPromoteToAirPods(bool degraded)
    {
        // No fallback exists: every mode collapses to Call-mode's manual toggle.
        if (degraded)
        {
            return _callModeActive;
        }

        return _mode switch
        {
            MicPolicyMode.AutoSwitch => _commsCaptureOpen,
            MicPolicyMode.CallMode => _callModeActive,
            _ => false, // HiFi-lock: comms always on the fallback
        };
    }

    private void AssignMediaRender(AudioEndpoint? airPodsRender)
    {
        if (airPodsRender is null)
        {
            return;
        }

        SetDefaultIfChanged(airPodsRender, AudioRole.Console);
        SetDefaultIfChanged(airPodsRender, AudioRole.Multimedia);
    }

    private void AssignComms(AudioEndpoint? render, AudioEndpoint? capture)
    {
        if (render is not null)
        {
            SetDefaultIfChanged(render, AudioRole.Communications);
        }

        if (capture is not null)
        {
            SetDefaultIfChanged(capture, AudioRole.Communications);
        }
    }

    // Assigns the role's default ONLY when it differs from the current assignment. This
    // idempotence is load-bearing, not a micro-optimisation. ApplyLocked re-runs on every
    // EndpointsChanged, and that event fires (via WindowsAudioEndpointChangeMonitor's
    // OnDefaultDeviceChanged) after our OWN SetDefaultEndpoint calls. IF IPolicyConfig
    // re-notifies on a set to the already-default device — the suspected mechanism behind
    // the chopped-playback report, though the OS contract does not document no-op
    // notifications — an unconditional re-apply self-triggers indefinitely, and every
    // re-set tears down and re-initialises the (A2DP Bluetooth) render stream. Converging
    // apply to a fixed point (no redundant set once satisfied) breaks that cycle and is
    // correct and harmless even if a no-op set turns out to be silent.
    private void SetDefaultIfChanged(AudioEndpoint endpoint, AudioRole role)
    {
        if (_audioPolicy.GetDefaultEndpoint(role, endpoint.Direction) != endpoint.Id)
        {
            _audioPolicy.SetDefaultEndpoint(endpoint.Id, role);
        }
    }

    private static AudioEndpoint? FindAirPods(
        IReadOnlyList<AudioEndpoint> endpoints, AudioEndpointDirection direction)
        => endpoints.FirstOrDefault(e => e.Direction == direction && e.IsAirPods);

    // Fallback = the current non-AirPods default-communications endpoint for this
    // direction, else the first available non-AirPods endpoint (spec prior decision).
    private AudioEndpoint? FindFallback(
        IReadOnlyList<AudioEndpoint> endpoints, AudioEndpointDirection direction)
    {
        var currentComms = _audioPolicy.GetDefaultEndpoint(AudioRole.Communications, direction);
        var preferred = endpoints.FirstOrDefault(
            e => e.Direction == direction && !e.IsAirPods && e.Id == currentComms);
        return preferred
            ?? endpoints.FirstOrDefault(e => e.Direction == direction && !e.IsAirPods);
    }
}
