namespace PodBridge.Core.Audio;

/// <summary>
/// Enumerates audio endpoints and sets the default endpoint <b>per role</b>
/// (default vs default-communications). Backs the Tier-1 microphone-profile policy
/// (HiFi-lock / auto-switch / call-mode); implemented on Windows by
/// <c>WindowsAudioPolicy</c> (NAudio enumerate + the undocumented
/// <c>IPolicyConfig</c>/<c>IPolicyConfig2</c> P/Invoke). No OS type leaks into Core;
/// the direction of a role default is implicit in the endpoint's
/// <see cref="AudioEndpoint.Direction"/>.
/// </summary>
public interface IAudioPolicy
{
    /// <summary>
    /// Enumerates the currently-available render and capture endpoints, each tagged
    /// with an adapter-supplied <see cref="AudioEndpoint.IsAirPods"/> flag. Called on
    /// demand (device-topology change / policy re-evaluation), never null.
    /// </summary>
    IReadOnlyList<AudioEndpoint> GetEndpoints();

    /// <summary>
    /// Gets the id of the endpoint currently holding <paramref name="role"/> for the
    /// given <paramref name="direction"/>, or <c>null</c> if none. Used to prefer the
    /// user's existing default-communications device when picking a fallback.
    /// </summary>
    string? GetDefaultEndpoint(AudioRole role, AudioEndpointDirection direction);

    /// <summary>
    /// Makes the endpoint with <paramref name="endpointId"/> the default for
    /// <paramref name="role"/>. The endpoint's direction (render vs capture) is
    /// implicit in the id, so a render and a capture id set the same role independently.
    /// </summary>
    void SetDefaultEndpoint(string endpointId, AudioRole role);
}
