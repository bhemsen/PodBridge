using PodBridge.Core.Media;
using PodBridge.Core.Tests.Bluetooth;
using Xunit;

namespace PodBridge.Core.Tests.Media;

/// <summary>
/// Device-independent tests for <see cref="AutoPlayPauseEngine"/>: pause-on-remove,
/// resume-on-reinsert, the "don't resume user-paused media" rule, and the hard
/// connection gate — all driven through a fake provider + fake media controller +
/// fake connection monitor (constitution Tier-1 test gate, no physical AirPods).
/// </summary>
public class AutoPlayPauseEngineTests
{
    [Fact]
    public void BudRemovedWhilePlaying_PausesMedia()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);   // buds in
        provider.SetInEar(false);  // bud removed → pause

        Assert.Equal(1, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    [Fact]
    public void BudReinsertedAfterOurPause_ResumesMedia()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);
        provider.SetInEar(false);  // pause (paused-by-us)
        provider.SetInEar(true);   // re-insert → resume

        Assert.Equal(1, media.PauseCallCount);
        Assert.Equal(1, media.ResumeCallCount);
    }

    [Fact]
    public void UserPausedMedia_IsNotAutoResumed()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Paused }; // user paused
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);
        provider.SetInEar(false);  // not playing → no pause, no paused-by-us flag
        provider.SetInEar(true);   // re-insert → must NOT resume user-paused media

        Assert.Equal(0, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    [Fact]
    public void BudRemovedWhileNotPlaying_DoesNotPause()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.None };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);
        provider.SetInEar(false);

        Assert.Equal(0, media.PauseCallCount);
    }

    [Fact]
    public void NoInEarTransition_TakesNoAction()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);  // false→true reinsert, but nothing was paused
        provider.SetInEar(true);  // repeat: no transition

        Assert.Equal(0, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    [Fact]
    public void Disconnected_FiresNoCalls_EvenWithInEarTransitions()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = new FakeConnectionMonitor();
        monitor.SimulateNoDevice(); // no AirPods connected
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);
        provider.SetInEar(false);
        provider.SetInEar(true);

        Assert.Equal(0, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    [Fact]
    public void GateReleasedOnConnect_ResumesNormalOperation()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = new FakeConnectionMonitor();
        monitor.SimulateDisconnected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(true);
        provider.SetInEar(false);  // gated out → no pause
        Assert.Equal(0, media.PauseCallCount);

        monitor.SimulateConnected();
        provider.SetInEar(true);   // in-ear baseline still false → reinsert, nothing paused
        provider.SetInEar(false);  // now connected → pause fires

        Assert.Equal(1, media.PauseCallCount);
    }

    private static FakeConnectionMonitor Connected()
    {
        var monitor = new FakeConnectionMonitor();
        monitor.SimulateConnected();
        return monitor;
    }
}
