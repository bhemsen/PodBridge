namespace PodBridge.Core.Audio;

/// <summary>
/// Forces the AirPods Hands-Free (HFP/SCO) link up while the AirPods hold the
/// communications role, so their <b>capture</b> (microphone) endpoint comes live.
/// <para>
/// A routing-role assignment alone (<see cref="IAudioPolicy.SetDefaultEndpoint"/> via
/// the undocumented <c>IPolicyConfig</c>) never opens a stream, so it never wakes the
/// HFP link — Windows brings the AirPods mic endpoint live only when an app opens a
/// stream on the device: a capture stream, OR a <i>render</i> stream tagged
/// <c>AudioCategory_Communications</c>. PodBridge therefore proactively holds a silent
/// Communications-category <b>render</b> keep-alive on the AirPods render endpoint while
/// they hold comms; the resulting topology change surfaces the now-live capture endpoint,
/// which <see cref="MicPolicyEngine"/> then assigns as the comms-capture default.
/// </para>
/// <para>
/// <b>Render only.</b> Implementations MUST open a render (playback) stream only and MUST
/// NEVER open a capture/microphone stream — the mic endpoint is engaged as a side effect
/// of HFP coming up, not by us capturing. Tier 1: no admin, no driver.
/// </para>
/// <para>
/// <b>Graceful degradation.</b> No method throws; any OS/COM failure degrades to a no-op
/// (constitution: never crash the tray).
/// </para>
/// </summary>
public interface ICommsProfileEngager
{
    /// <summary>
    /// Opens and holds a silent Communications-category render keep-alive on the render
    /// endpoint identified by <paramref name="renderEndpointId"/> (the AirPods render
    /// endpoint id from <see cref="AudioEndpoint.Id"/>). Idempotent: engaging the
    /// already-engaged id is a no-op; engaging a different id releases the current
    /// keep-alive and opens the new one. Never throws.
    /// </summary>
    void Engage(string renderEndpointId);

    /// <summary>
    /// Releases the held keep-alive stream (letting Windows drop the HFP link when nothing
    /// else needs the mic). A no-op when nothing is engaged. Never throws.
    /// </summary>
    void Release();
}
