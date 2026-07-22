using PodBridge.Core.Audio;
using Xunit;

namespace PodBridge.Core.Tests.Audio;

/// <summary>
/// Device-independent guards (constitution Tier-1 gate) over the honest audio-collapse
/// recovery copy the App renders (issue #173). Pins the honesty invariants: the
/// explanation states this is a Windows-level failure, not a PodBridge bug, and that
/// PodBridge cannot fix it directly (no admin, no driver); the steps name both services
/// restarted via services.msc and state the admin requirement comes from Windows, not
/// PodBridge.
/// </summary>
public class AudioCollapseGuidanceTests
{
    [Fact]
    public void Explanation_states_this_is_a_Windows_level_issue_not_a_PodBridge_bug()
    {
        Assert.Contains("Windows-level", AudioCollapseGuidance.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a bug in PodBridge", AudioCollapseGuidance.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explanation_says_PodBridge_cannot_fix_it_directly()
        => Assert.Contains(
            "can't fix it directly", AudioCollapseGuidance.Explanation, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Explanation_makes_no_admin_or_driver_claim()
    {
        Assert.Contains("no administrator rights", AudioCollapseGuidance.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no driver", AudioCollapseGuidance.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Step1_recommends_restarting_the_PC()
        => Assert.Contains("Restart your PC", AudioCollapseGuidance.Step1Title, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Step2_names_both_services_by_their_service_names()
    {
        Assert.Contains("Windows Audio", AudioCollapseGuidance.Step2Title, StringComparison.Ordinal);
        Assert.Contains("Windows Audio Endpoint Builder", AudioCollapseGuidance.Step2Title, StringComparison.Ordinal);
        Assert.Contains("Audiosrv", AudioCollapseGuidance.Step2Body, StringComparison.Ordinal);
        Assert.Contains("AudioEndpointBuilder", AudioCollapseGuidance.Step2Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Step2_says_the_admin_prompt_comes_from_Windows_not_PodBridge()
        => Assert.Contains(
            "Windows will prompt you for that, not PodBridge",
            AudioCollapseGuidance.Step2Body,
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Step3_recommends_a_full_shutdown_over_sleep_or_hibernate()
    {
        Assert.Contains("shut down", AudioCollapseGuidance.Step3Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sleeping", AudioCollapseGuidance.Step3Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hibernating", AudioCollapseGuidance.Step3Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NotificationTitle_is_honest_about_what_happened()
        => Assert.Contains(
            "lost your audio devices", AudioCollapseGuidance.NotificationTitle, StringComparison.OrdinalIgnoreCase);
}
