namespace PodBridge.Core.Audio;

/// <summary>
/// Detects a Windows audio-stack collapse — the whole render+capture endpoint set
/// vanishing after a long sleep/resume under heavy audio-session load, a Windows Audio
/// Endpoint Builder re-enumeration failure that PodBridge cannot cause (no
/// device-removal capability, no power/resume-event subscription) and cannot fix (no
/// admin, no driver in Tier 1). Hooks the existing <see cref="IAudioEndpointChangeMonitor"/>
/// (issue #173) purely as a "something changed, go look" signal; the actual condition is
/// read from <see cref="IAudioPolicy.GetEndpoints"/>.
/// <list type="bullet">
/// <item><b>Threshold:</b> collapsed means the enumerable render+capture endpoint count
/// is zero — with zero endpoints no default can resolve either, so this single check
/// covers both symptoms the issue describes. A normal single-device add/remove, a
/// default-device switch, or an AirPods reconnect never zeroes the whole system's
/// endpoint count (the built-in/other devices remain), so none of them false-trigger.</item>
/// <item><b>Debounce:</b> every topology change (re)starts a short settle timer; the
/// condition is evaluated once the storm of events quiets down, so a burst of
/// add/remove/default-changed notifications during one real transition is coalesced
/// into a single check instead of one per event.</item>
/// <item><b>Edge-triggered, once per episode:</b> <see cref="CollapseDetected"/> fires
/// only on the transition INTO the collapsed state. Once collapsed, further debounced
/// checks that still find it collapsed raise nothing more; once endpoints reappear the
/// detector re-arms silently, so a later episode fires again.</item>
/// </list>
/// </summary>
public sealed class AudioCollapseDetector : IDisposable
{
    /// <summary>
    /// Default settle window after the last topology-change notification before the
    /// collapse condition is (re-)evaluated.
    /// </summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(3);

    private readonly IAudioPolicy _audioPolicy;
    private readonly IAudioEndpointChangeMonitor _endpointChangeMonitor;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounce;
    private readonly ITimer _debounceTimer;
    private readonly Lock _gate = new();

    private bool _collapsed;

    /// <summary>
    /// Wires the detector to its endpoint-change signal. It only subscribes; starting
    /// the underlying monitor stays the composition root's responsibility.
    /// </summary>
    /// <param name="audioPolicy">Endpoint enumeration used to read the current count.</param>
    /// <param name="endpointChangeMonitor">Device-topology change source; each change
    /// (re)starts the debounce timer.</param>
    /// <param name="timeProvider">Clock/timer source; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="debounce">Settle window; defaults to <see cref="DefaultDebounce"/>.</param>
    public AudioCollapseDetector(
        IAudioPolicy audioPolicy,
        IAudioEndpointChangeMonitor endpointChangeMonitor,
        TimeProvider? timeProvider = null,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(audioPolicy);
        ArgumentNullException.ThrowIfNull(endpointChangeMonitor);

        _audioPolicy = audioPolicy;
        _endpointChangeMonitor = endpointChangeMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _debounce = debounce ?? DefaultDebounce;
        _debounceTimer = _timeProvider.CreateTimer(
            OnDebounceTick, state: null, dueTime: Timeout.InfiniteTimeSpan, period: Timeout.InfiniteTimeSpan);

        _endpointChangeMonitor.EndpointsChanged += OnEndpointsChanged;
    }

    /// <summary>
    /// Raised once per collapse episode (edge-triggered) after the debounce settles with
    /// the render+capture endpoint count still at zero. May fire on a background/timer
    /// thread; handlers must marshal to the UI thread themselves.
    /// </summary>
    public event EventHandler? CollapseDetected;

    /// <summary>Unsubscribes from the endpoint-change monitor and disposes the debounce timer.</summary>
    public void Dispose()
    {
        _endpointChangeMonitor.EndpointsChanged -= OnEndpointsChanged;
        _debounceTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    // Every topology change restarts the settle window rather than checking immediately,
    // so a burst of add/remove/default-changed events from one transition collapses into
    // a single evaluation instead of one per event.
    private void OnEndpointsChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            _debounceTimer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceTick(object? state)
    {
        bool raise;
        lock (_gate)
        {
            var isCollapsed = _audioPolicy.GetEndpoints().Count == 0;
            raise = isCollapsed && !_collapsed;
            _collapsed = isCollapsed;
        }

        if (raise)
        {
            CollapseDetected?.Invoke(this, EventArgs.Empty);
        }
    }
}
