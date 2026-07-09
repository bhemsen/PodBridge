using PodBridge.Core.Media;
using PodBridge.Core.Tests.Bluetooth;
using Xunit;

namespace PodBridge.Core.Tests.Media;

/// <summary>
/// Device-independent tests for <see cref="AutoPlayPauseEngine"/>: pause on the
/// first bud removed, resume when both buds are back in, the "don't resume
/// user-paused media" rule, and the hard connection gate — all driven through a
/// fake provider + fake media controller + fake connection monitor (constitution
/// Tier-1 test gate, no physical AirPods). "Wearing" == both buds in an ear.
/// </summary>
public class AutoPlayPauseEngineTests
{
    [Fact]
    public void FirstBudRemovedWhilePlaying_PausesMedia()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetBothInEar(true);   // both buds in
        provider.SetBothInEar(false);  // buds removed → pause

        Assert.Equal(1, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    // Decisive case: removing ONE bud (the "first bud out" trigger) must pause and
    // re-inserting it must resume. This FAILS under an AnyInEar trigger (one bud in
    // keeps AnyInEar true, so no transition fires) and PASSES under BothInEar.
    [Fact]
    public void OneBudRemovedWhilePlaying_PausesThenResumesOnReinsert()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetInEar(left: true, right: true);   // wearing both
        provider.SetInEar(left: true, right: false);  // remove ONE bud → pause
        Assert.Equal(1, media.PauseCallCount);

        provider.SetInEar(left: true, right: true);   // put it back → resume
        Assert.Equal(1, media.ResumeCallCount);
    }

    [Fact]
    public void BothBudsBackInAfterOurPause_ResumesMedia()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetBothInEar(true);
        provider.SetBothInEar(false);  // pause (paused-by-us)
        provider.SetBothInEar(true);   // back in → resume

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

        provider.SetBothInEar(true);
        provider.SetBothInEar(false);  // not playing → no pause, no paused-by-us flag
        provider.SetBothInEar(true);   // back in → must NOT resume user-paused media

        Assert.Equal(0, media.PauseCallCount);
        Assert.Equal(0, media.ResumeCallCount);
    }

    [Fact]
    public void FirstBudRemovedWhileNotPlaying_DoesNotPause()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.None };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetBothInEar(true);
        provider.SetBothInEar(false);

        Assert.Equal(0, media.PauseCallCount);
    }

    [Fact]
    public void NoBothInEarTransition_TakesNoAction()
    {
        var provider = new FakeDeviceStateProvider();
        var media = new FakeMediaController { PlaybackState = PlaybackState.Playing };
        var monitor = Connected();
        using var engine = new AutoPlayPauseEngine(provider, media, monitor);

        provider.SetBothInEar(true);  // false→true both-in, but nothing was paused
        provider.SetBothInEar(true);  // repeat: no transition

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

        provider.SetBothInEar(true);
        provider.SetBothInEar(false);
        provider.SetBothInEar(true);

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

        provider.SetBothInEar(true);
        provider.SetBothInEar(false);  // gated out → no pause
        Assert.Equal(0, media.PauseCallCount);

        monitor.SimulateConnected();
        provider.SetBothInEar(true);   // both-in baseline still false → nothing paused
        provider.SetBothInEar(false);  // now connected → pause fires

        Assert.Equal(1, media.PauseCallCount);
    }

    private static FakeConnectionMonitor Connected()
    {
        var monitor = new FakeConnectionMonitor();
        monitor.SimulateConnected();
        return monitor;
    }
}
