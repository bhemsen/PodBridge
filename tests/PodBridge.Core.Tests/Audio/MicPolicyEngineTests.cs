using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for <see cref="MicPolicyEngine"/>,
/// driven through a fake <see cref="IAudioPolicy"/> + fake <see cref="IAudioSessionMonitor"/>.
/// Covers HiFi-lock assignment, Auto-switch promote-on-open + restore-on-close, the
/// Call-mode manual swap, the fallback-preference rule, and the single-device degrade
/// decision (Call-mode behaviour + honest warning, never a silent HFP forcing).
/// </summary>
public class MicPolicyEngineTests
{
    private const string ApRender = "ap-render";
    private const string ApCapture = "ap-capture";
    private const string SpkRender = "spk-render";
    private const string MicCapture = "mic-capture";

    // ---- HiFi-lock -------------------------------------------------------------

    [Fact]
    public void HiFiLock_IsDefaultMode_AndRoutesMediaToAirPodsCommsToFallback()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);

        Assert.Equal(MicPolicyMode.HiFiLock, engine.CurrentMode);
        Assert.False(engine.NoAlternateMicWarning);
        // AirPods = default media (Console + Multimedia) render.
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Console));
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));
        // Comms render + capture = the non-AirPods fallback (media stays A2DP).
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    // ---- Auto-switch -----------------------------------------------------------

    [Fact]
    public void AutoSwitch_PromotesAirPodsOnSessionOpen_AndRestoresHiFiLockOnClose()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        engine.SetMode(MicPolicyMode.AutoSwitch);

        // Idle Auto-switch == HiFi-lock assignment.
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));

        monitor.RaiseCaptureStarted();
        // Promoted: comms render + capture now on the AirPods; media untouched.
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(ApCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));

        monitor.RaiseCaptureStopped();
        // Restored deterministically to the HiFi-lock assignment (fallback), not "whatever
        // was there": the fallback picker ignores the AirPods that briefly held comms.
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    // ---- Idempotence / no feedback loop ---------------------------------------

    [Fact]
    public void SteadyState_RedundantRefresh_IssuesNoFurtherSets()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);

        // The constructor's initial apply established the HiFi-lock assignment.
        var callsAfterInitialApply = policy.SetCalls.Count;

        // A redundant Refresh — exactly what a self-triggered OnDefaultDeviceChanged causes
        // on real hardware — must issue ZERO further SetDefaultEndpoint calls once at steady
        // state. Otherwise each re-set re-fires the OS default-changed notification and the
        // apply loops, continuously re-initialising the Bluetooth render stream (chopped
        // playback — the post-Phase-4 regression this idempotence guards against).
        engine.Refresh();
        engine.Refresh();

        Assert.Equal(callsAfterInitialApply, policy.SetCalls.Count);
    }

    [Fact]
    public void HiFiLock_IgnoresCommsSession_KeepingCommsOnFallback()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor); // stays HiFi-lock

        monitor.RaiseCaptureStarted();

        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    // ---- Call-mode -------------------------------------------------------------

    [Fact]
    public void CallMode_ToggleSwapsCommsRoleToAndFromAirPods()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        engine.SetMode(MicPolicyMode.CallMode);

        // Toggle off (default): comms on the fallback (A2DP-preferred).
        Assert.False(engine.CallModeActive);
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));

        engine.SetCallModeActive(true);
        Assert.True(engine.CallModeActive);
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(ApCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));

        engine.SetCallModeActive(false);
        Assert.Equal(SpkRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(MicCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    // ---- Fallback selection ----------------------------------------------------

    [Fact]
    public void Fallback_PrefersCurrentCommsDefault_OverFirstNonAirPods()
    {
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        policy.Add("spk-first", AudioEndpointDirection.Render, isAirPods: false);
        var preferred = policy.Add("spk-second", AudioEndpointDirection.Render, isAirPods: false);
        policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        // The user's existing default-communications render device is the SECOND one.
        policy.SeedDefault(preferred, AudioRole.Communications);

        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor); // HiFi-lock

        Assert.Equal("spk-second", policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
    }

    // ---- Single-device degrade -------------------------------------------------

    [Fact]
    public void SingleDevice_HiFiLock_DegradesToWarning_WithNoSilentHfp()
    {
        var policy = AirPodsOnlyPolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor); // HiFi-lock, only AirPods

        Assert.True(engine.NoAlternateMicWarning);
        // Media render still the AirPods.
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Multimedia));
        // The comms role is NEVER assigned on its own — no silent HFP forcing.
        Assert.DoesNotContain(policy.SetCalls, c => c.Role == AudioRole.Communications);
    }

    [Fact]
    public void SingleDevice_AutoSwitch_SessionOpen_StillDoesNotForceHfp()
    {
        var policy = AirPodsOnlyPolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        engine.SetMode(MicPolicyMode.AutoSwitch);

        monitor.RaiseCaptureStarted(); // would promote if a fallback existed

        Assert.True(engine.NoAlternateMicWarning);
        Assert.DoesNotContain(
            policy.SetCalls,
            c => c.Role == AudioRole.Communications && c.Direction == AudioEndpointDirection.Capture);
    }

    [Fact]
    public void SingleDevice_ManualCallModeToggle_RoutesCommsToAirPods_WarningStays()
    {
        var policy = AirPodsOnlyPolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);

        engine.SetCallModeActive(true); // user explicitly opts into the AirPods mic

        Assert.True(engine.NoAlternateMicWarning); // there is still no alternate mic
        Assert.Equal(ApRender, policy.DefaultId(AudioEndpointDirection.Render, AudioRole.Communications));
        Assert.Equal(ApCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    // ---- Degrade warning signal ------------------------------------------------

    [Fact]
    public void NoAlternateMicWarning_FiresOnceOnTransitionToDegraded()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        Assert.False(engine.NoAlternateMicWarning);

        var events = new List<bool>();
        engine.NoAlternateMicWarningChanged += (_, on) => events.Add(on);

        policy.Remove(SpkRender);
        policy.Remove(MicCapture);
        engine.Refresh(); // topology changed: AirPods now the only device

        Assert.True(engine.NoAlternateMicWarning);
        Assert.True(Assert.Single(events)); // fired exactly once, with value true

        engine.Refresh(); // still degraded → no duplicate event
        Assert.Single(events);
    }

    [Fact]
    public void EndpointChange_FromMonitor_TriggersRefresh_UpdatingDegradeWarning()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        var changeMonitor = new FakeAudioEndpointChangeMonitor();
        using var engine = new MicPolicyEngine(
            policy, monitor, changeMonitor, new FakeCommsProfileEngager());
        Assert.False(engine.NoAlternateMicWarning);

        var events = new List<bool>();
        engine.NoAlternateMicWarningChanged += (_, on) => events.Add(on);

        // The fallback mic is unplugged, then the topology-change monitor fires: the
        // engine must Refresh on its own (no explicit Refresh call) and degrade.
        policy.Remove(SpkRender);
        policy.Remove(MicCapture);
        changeMonitor.RaiseEndpointsChanged();

        Assert.True(engine.NoAlternateMicWarning);
        Assert.True(Assert.Single(events)); // fired exactly once, with value true

        // Fallback returns and the monitor fires again: the warning clears live.
        policy.Add(SpkRender, AudioEndpointDirection.Render, isAirPods: false);
        policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        changeMonitor.RaiseEndpointsChanged();

        Assert.False(engine.NoAlternateMicWarning);
        Assert.Collection(events, Assert.True, Assert.False); // true then false, exactly two
    }

    [Fact]
    public void NoAlternateMicWarningText_IsHonestAboutTheMicLimit()
    {
        Assert.Contains("mic", MicPolicyEngine.NoAlternateMicWarningText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HFP", MicPolicyEngine.NoAlternateMicWarningText, StringComparison.Ordinal);
    }

    // ---- Fixtures --------------------------------------------------------------

    // Constructs the engine with a real audio-session monitor fake plus an inert
    // topology-change monitor (the tests that exercise topology changes construct the
    // engine explicitly with a named change monitor instead).
    private static MicPolicyEngine NewEngine(
        FakeAudioPolicy policy,
        FakeAudioSessionMonitor monitor,
        FakeCommsProfileEngager? engager = null)
        => new(
            policy,
            monitor,
            new FakeAudioEndpointChangeMonitor(),
            engager ?? new FakeCommsProfileEngager());

    // AirPods (render + capture) plus a non-AirPods fallback (render + capture).
    private static FakeAudioPolicy TwoDevicePolicy()
    {
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        policy.Add(SpkRender, AudioEndpointDirection.Render, isAirPods: false);
        policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        return policy;
    }

    // Only the AirPods endpoints exist — no non-AirPods fallback for the comms role.
    private static FakeAudioPolicy AirPodsOnlyPolicy()
    {
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        return policy;
    }
}
