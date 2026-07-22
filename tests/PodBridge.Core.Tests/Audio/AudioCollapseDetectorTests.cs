using Microsoft.Extensions.Time.Testing;
using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent tests (constitution Tier-1 gate) for
/// <see cref="AudioCollapseDetector"/>: the collapse threshold (including the
/// AirPods-only false-positive guard), the debounce that coalesces a burst of
/// topology-change events, that normal transitions never false-trigger, and the
/// edge-triggered once-per-episode + re-arm behaviour (issue #173).
/// </summary>
public class AudioCollapseDetectorTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    [Fact]
    public void AllEndpointsGone_RaisesCollapseDetectedOnceAfterDebounce()
    {
        var (policy, monitor, time, detector, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        policy.Add("mic", AudioEndpointDirection.Capture, isAirPods: false);
        monitor.RaiseEndpointsChanged(); // let the detector observe the pre-collapse mix
        time.Advance(Debounce);
        Assert.Empty(raised);

        policy.Remove("speakers");
        policy.Remove("mic");
        monitor.RaiseEndpointsChanged();

        Assert.Empty(raised); // not yet — the debounce window has not elapsed
        time.Advance(Debounce);

        Assert.Single(raised);
    }

    [Fact]
    public void SingleDeviceRemove_NeverTriggers()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        var mic = policy.Add("mic", AudioEndpointDirection.Capture, isAirPods: false);
        _ = mic;
        var airpods = policy.Add("airpods-render", AudioEndpointDirection.Render, isAirPods: true);
        _ = airpods;

        policy.Remove("mic"); // one capture endpoint removed; render endpoints remain
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Empty(raised);
    }

    [Fact]
    public void DefaultDeviceSwitch_NeverTriggers()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        var a = policy.Add("speakers-a", AudioEndpointDirection.Render, isAirPods: false);
        var b = policy.Add("speakers-b", AudioEndpointDirection.Render, isAirPods: false);
        policy.SeedDefault(a, AudioRole.Multimedia);

        // Switching the default render device changes no endpoint count.
        policy.SetDefaultEndpoint(b.Id, AudioRole.Multimedia);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Empty(raised);
    }

    [Fact]
    public void AirPodsReconnect_NeverTriggers()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        var airpodsRender = policy.Add("airpods-render", AudioEndpointDirection.Render, isAirPods: true);

        // Disconnect (AirPods endpoints vanish) then reconnect — the fallback device
        // keeps the total count above zero throughout.
        policy.Remove(airpodsRender.Id);
        monitor.RaiseEndpointsChanged();
        policy.Add("airpods-render", AudioEndpointDirection.Render, isAirPods: true);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Empty(raised);
    }

    [Fact]
    public void AirPodsOnlyMachine_AirPodsDisconnect_NeverTriggers()
    {
        // A machine whose ONLY audio device is the AirPods (e.g. no onboard audio).
        var (policy, monitor, time, _, raised) = CreateSut();
        var airPodsRender = policy.Add("airpods-render", AudioEndpointDirection.Render, isAirPods: true);
        var airPodsCapture = policy.Add("airpods-capture", AudioEndpointDirection.Capture, isAirPods: true);
        monitor.RaiseEndpointsChanged(); // let the detector observe the AirPods-only mix
        time.Advance(Debounce);
        Assert.Empty(raised);

        // An ordinary AirPods disconnect also drops the count to zero here, but no
        // non-AirPods device vanished — this must NOT read as a Windows-level collapse.
        policy.Remove(airPodsRender.Id);
        policy.Remove(airPodsCapture.Id);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Empty(raised);
    }

    [Fact]
    public void BurstOfEvents_CoalescesIntoOneEvaluation()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        monitor.RaiseEndpointsChanged(); // let the detector observe the pre-collapse mix
        time.Advance(Debounce);
        Assert.Empty(raised);

        policy.Remove("speakers");

        // A storm of change notifications for the same underlying transition.
        monitor.RaiseEndpointsChanged();
        time.Advance(TimeSpan.FromSeconds(1));
        monitor.RaiseEndpointsChanged();
        time.Advance(TimeSpan.FromSeconds(1));
        monitor.RaiseEndpointsChanged();

        // Each event restarts the debounce, so it has not elapsed since the last one yet.
        Assert.Empty(raised);

        time.Advance(Debounce);

        Assert.Single(raised);
    }

    [Fact]
    public void Collapsed_StaysSilentOnFurtherChecksUntilRecovered()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);
        Assert.Empty(raised);

        policy.Remove("speakers");
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);
        Assert.Single(raised);

        // Still collapsed on the next debounced check: no second event for the same episode.
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Single(raised);
    }

    [Fact]
    public void Recovery_ReArmsSoALaterCollapseFiresAgain()
    {
        var (policy, monitor, time, _, raised) = CreateSut();
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);
        Assert.Empty(raised);

        policy.Remove("speakers");
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);
        Assert.Single(raised);

        // Recovery: the endpoint reappears.
        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);
        Assert.Single(raised); // recovery itself never raises anything

        // A second, independent episode.
        policy.Remove("speakers");
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTheMonitor()
    {
        var (policy, monitor, time, detector, raised) = CreateSut();
        detector.Dispose();

        policy.Add("speakers", AudioEndpointDirection.Render, isAirPods: false);
        policy.Remove("speakers");
        monitor.RaiseEndpointsChanged();
        time.Advance(Debounce);

        Assert.Empty(raised);
    }

    private static (
        FakeAudioPolicy Policy,
        FakeAudioEndpointChangeMonitor Monitor,
        FakeTimeProvider Time,
        AudioCollapseDetector Detector,
        List<EventArgs> Raised) CreateSut()
    {
        var policy = new FakeAudioPolicy();
        var monitor = new FakeAudioEndpointChangeMonitor();
        var time = new FakeTimeProvider();
        var detector = new AudioCollapseDetector(policy, monitor, time, Debounce);
        var raised = new List<EventArgs>();
        detector.CollapseDetected += (_, e) => raised.Add(e);
        return (policy, monitor, time, detector, raised);
    }
}
