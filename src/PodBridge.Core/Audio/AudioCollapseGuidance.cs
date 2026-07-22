namespace PodBridge.Core.Audio;

/// <summary>
/// Device-independent, honest user-facing copy for the audio-stack-collapse recovery
/// guide (issue #173). After a long sleep/resume under heavy audio-session load, Windows'
/// own Audio Endpoint Builder can fail to re-enumerate the device set — a Windows-level
/// failure <see cref="AudioCollapseDetector"/> detects but PodBridge neither causes (no
/// device-removal capability, no power/resume-event subscription) nor fixes (no admin, no
/// driver in Tier 1). This copy states that plainly (constitution: honest audio surface)
/// and gives step-by-step recovery guidance; kept in Core so the honesty invariants are
/// covered by the constitution's Tier-1 device-independent test gate (see
/// <c>AudioCollapseGuidanceTests</c>). The App (<c>TrayAudioCollapseController</c>,
/// <c>AudioRecoveryWindow</c>) renders these strings verbatim.
/// </summary>
public static class AudioCollapseGuidance
{
    /// <summary>Tray-notification title, shown once per collapse episode.</summary>
    public const string NotificationTitle = "Windows lost your audio devices";

    /// <summary>Tray-notification body; clicking it opens the in-app recovery guide.</summary>
    public const string NotificationMessage = "Click for how to fix.";

    /// <summary>Recovery-window heading.</summary>
    public const string Title = "Windows lost your audio devices";

    /// <summary>
    /// The honest root-cause statement: a Windows-level failure, not a PodBridge bug, that
    /// PodBridge cannot fix directly (no admin, no driver by default).
    /// </summary>
    public const string Explanation =
        "After a long sleep/resume — especially under heavy audio load — Windows' own audio "
        + "service can fail to re-enumerate your sound devices, so they all disappear at once "
        + "(even ones PodBridge never touches, like a wired line-out). This is a Windows-level "
        + "issue, not a bug in PodBridge, and PodBridge can't fix it directly: it has no "
        + "administrator rights and installs no driver by default.";

    /// <summary>Step 1 heading: the simplest fix.</summary>
    public const string Step1Title = "1. Restart your PC";

    /// <summary>Step 1 body.</summary>
    public const string Step1Body = "The simplest and most reliable fix.";

    /// <summary>Step 2 heading: the two services to restart.</summary>
    public const string Step2Title =
        "2. Restart the \"Windows Audio\" and \"Windows Audio Endpoint Builder\" services";

    /// <summary>
    /// Step 2 body: names both services (Audiosrv, AudioEndpointBuilder), states the
    /// admin requirement, and makes clear the prompt comes from Windows, not PodBridge.
    /// </summary>
    public const string Step2Body =
        "Open Services (below), find \"Windows Audio\" (Audiosrv) and \"Windows Audio Endpoint "
        + "Builder\" (AudioEndpointBuilder), right-click each and choose Restart. This needs "
        + "administrator rights — Windows will prompt you for that, not PodBridge.";

    /// <summary>Label for the button that shell-launches <c>services.msc</c>.</summary>
    public const string OpenServicesButtonLabel = "Open Services (services.msc)";

    /// <summary>Step 3 heading: the preventive tip.</summary>
    public const string Step3Title = "Tip: shut down instead of sleeping";

    /// <summary>Step 3 body.</summary>
    public const string Step3Body =
        "Between long idle periods, a full shutdown avoids the resume path that triggers this, "
        + "rather than sleep or hibernate.";
}
