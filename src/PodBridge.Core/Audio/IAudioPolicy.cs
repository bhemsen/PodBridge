namespace PodBridge.Core.Audio;

/// <summary>Audio endpoint role, mapping to the Windows ERole enum.</summary>
public enum AudioRole
{
    Console,
    Multimedia,
    Communications,
}

/// <summary>
/// Sets and reads the default audio endpoint per role. Backs the Tier-1
/// microphone-profile policy (HiFi-lock / auto-switch / call-mode) via the
/// undocumented IPolicyConfig interface on Windows.
/// </summary>
public interface IAudioPolicy
{
    void SetDefaultEndpoint(string deviceId, AudioRole role);

    string? GetDefaultEndpoint(AudioRole role);
}
