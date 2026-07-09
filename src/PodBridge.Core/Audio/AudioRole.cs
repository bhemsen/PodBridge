namespace PodBridge.Core.Audio;

/// <summary>
/// Audio endpoint role, mirroring the Windows <c>ERole</c> enum. The default
/// (media) device is the <see cref="Console"/> + <see cref="Multimedia"/> pair;
/// <see cref="Communications"/> is the separate default-communications device.
/// The mic-profile policy routes AirPods vs a fallback device per role.
/// </summary>
public enum AudioRole
{
    /// <summary>System / general default device role (<c>eConsole</c>).</summary>
    Console,

    /// <summary>Media (music / video) default device role (<c>eMultimedia</c>).</summary>
    Multimedia,

    /// <summary>Voice-communications default device role (<c>eCommunications</c>).</summary>
    Communications,
}
