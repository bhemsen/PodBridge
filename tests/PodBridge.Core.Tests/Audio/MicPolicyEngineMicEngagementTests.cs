using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for the AirPods-mic engagement
/// behaviour of <see cref="MicPolicyEngine"/> (issue #156):
/// <list type="bullet">
/// <item><b>Candidate D</b> — the honest <see cref="MicPolicyEngine.AirPodsMicUnavailable"/>
/// warning fires (instead of a silent no-op) when a comms promotion is requested but there
/// is no active AirPods capture endpoint to set, and clears once one appears and is set.</item>
/// <item><b>Candidate A</b> — the engine drives <see cref="ICommsProfileEngager"/>: it
/// engages the AirPods render endpoint on a comms promotion (forcing HFP so the mic comes
/// live) and releases it on demotion, idempotently.</item>
/// </list>
/// </summary>
public class MicPolicyEngineMicEngagementTests
{
    private const string ApRender = "ap-render";
    private const string ApCapture = "ap-capture";
    private const string SpkRender = "spk-render";
    private const string MicCapture = "mic-capture";

    // ---- Candidate D: honest "AirPods mic unavailable" warning -----------------

    [Fact]
    public void AutoSwitch_PromoteWithNoAirPodsCapture_WarnsUnavailable_SetsNoCommsCapture()
    {
        var policy = NoAirPodsCapturePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        var events = new List<bool>();
        engine.AirPodsMicUnavailableChanged += (_, on) => events.Add(on);

        engine.SetMode(MicPolicyMode.AutoSwitch);
        monitor.RaiseCaptureStarted(); // promotion requested — but no AirPods capture endpoint

        Assert.True(engine.AirPodsMicUnavailable);
        Assert.DoesNotContain(
            policy.SetCalls,
            c => c.Role == AudioRole.Communications && c.Direction == AudioEndpointDirection.Capture);
        Assert.True(Assert.Single(events)); // fired exactly once, with value true
    }

    [Fact]
    public void CallMode_ToggleOnWithNoAirPodsCapture_WarnsUnavailable_SetsNoCommsCapture()
    {
        var policy = NoAirPodsCapturePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        var events = new List<bool>();
        engine.AirPodsMicUnavailableChanged += (_, on) => events.Add(on);

        engine.SetMode(MicPolicyMode.CallMode);
        engine.SetCallModeActive(true); // user opts into the AirPods mic — but none is exposed

        Assert.True(engine.AirPodsMicUnavailable);
        Assert.DoesNotContain(
            policy.SetCalls,
            c => c.Role == AudioRole.Communications && c.Direction == AudioEndpointDirection.Capture);
        Assert.True(Assert.Single(events)); // fired exactly once, with value true
    }

    [Fact]
    public void AirPodsMicUnavailable_Clears_WhenCaptureEndpointAppearsAndIsSet()
    {
        var policy = NoAirPodsCapturePolicy();
        var monitor = new FakeAudioSessionMonitor();
        using var engine = NewEngine(policy, monitor);
        engine.SetMode(MicPolicyMode.AutoSwitch);
        monitor.RaiseCaptureStarted();
        Assert.True(engine.AirPodsMicUnavailable);

        // The HFP link came up: the AirPods capture endpoint is now live. A topology change
        // re-applies the policy, which sets it as comms-capture default and clears the warning
        // (the self-healing loop candidate A relies on).
        policy.Add(ApCapture, AudioEndpointDirection.Capture, isAirPods: true);
        engine.Refresh();

        Assert.False(engine.AirPodsMicUnavailable);
        Assert.Equal(ApCapture, policy.DefaultId(AudioEndpointDirection.Capture, AudioRole.Communications));
    }

    [Fact]
    public void AirPodsMicUnavailableText_IsHonestAndActionable()
    {
        Assert.Contains("mic", MicPolicyEngine.AirPodsMicUnavailableText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable", MicPolicyEngine.AirPodsMicUnavailableText, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Candidate A: HFP engagement via the comms-profile engager -------------

    [Fact]
    public void AutoSwitch_PromoteEngagesAirPodsRender_CloseReleases()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        var engager = new FakeCommsProfileEngager();
        using var engine = NewEngine(policy, monitor, engager);
        engine.SetMode(MicPolicyMode.AutoSwitch);

        monitor.RaiseCaptureStarted(); // promote
        Assert.Equal(ApRender, engager.EngagedId);
        Assert.Equal([ApRender], engager.EngageCalls);

        monitor.RaiseCaptureStopped(); // demote
        Assert.Null(engager.EngagedId);
        Assert.Equal(1, engager.ReleaseCalls);
    }

    [Fact]
    public void CallMode_ToggleOnEngages_ToggleOffReleases()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        var engager = new FakeCommsProfileEngager();
        using var engine = NewEngine(policy, monitor, engager);
        engine.SetMode(MicPolicyMode.CallMode);

        engine.SetCallModeActive(true);
        Assert.Equal(ApRender, engager.EngagedId);

        engine.SetCallModeActive(false);
        Assert.Null(engager.EngagedId);
        Assert.Equal(1, engager.ReleaseCalls);
    }

    [Fact]
    public void HiFiLock_NeverEngages_EvenOnCommsSession()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        var engager = new FakeCommsProfileEngager();
        using var engine = NewEngine(policy, monitor, engager); // stays HiFi-lock

        monitor.RaiseCaptureStarted();

        Assert.Empty(engager.EngageCalls);
        Assert.Null(engager.EngagedId);
    }

    [Fact]
    public void Promotion_RedundantRefresh_DoesNotReEngage()
    {
        var policy = TwoDevicePolicy();
        var monitor = new FakeAudioSessionMonitor();
        var engager = new FakeCommsProfileEngager();
        using var engine = NewEngine(policy, monitor, engager);
        engine.SetMode(MicPolicyMode.AutoSwitch);
        monitor.RaiseCaptureStarted();

        engine.Refresh(); // a self-triggered OnDefaultDeviceChanged on real hardware
        engine.Refresh();

        Assert.Equal([ApRender], engager.EngageCalls); // engaged once, idempotently
    }

    // ---- Fixtures --------------------------------------------------------------

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

    // The common real case: an AirPods RENDER endpoint and a non-AirPods fallback
    // render+capture, but NO active AirPods capture endpoint. The fallback is seeded as the
    // user's current comms defaults, so the initial HiFi-lock apply is idempotent and records
    // no comms-capture set call — isolating the promotion behaviour the tests assert on.
    private static FakeAudioPolicy NoAirPodsCapturePolicy()
    {
        var policy = new FakeAudioPolicy();
        policy.Add(ApRender, AudioEndpointDirection.Render, isAirPods: true);
        var spk = policy.Add(SpkRender, AudioEndpointDirection.Render, isAirPods: false);
        var mic = policy.Add(MicCapture, AudioEndpointDirection.Capture, isAirPods: false);
        policy.SeedDefault(spk, AudioRole.Communications);
        policy.SeedDefault(mic, AudioRole.Communications);
        return policy;
    }
}
