using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// Additional, notification-capable Core Audio COM interop used by
// WindowsAudioSessionMonitor (#29) to detect a Communications-role capture
// (microphone) session opening/closing driver-free and admin-free, per
// docs/research/mic-profile-policy-comms-detection.md (#26). It complements the
// read-only surface in CoreAudioInterop.cs (which is intentionally NOT edited
// here — a sibling issue builds on it in parallel) by declaring the vtable slots
// that file omits: IMMDeviceEnumerator::GetDefaultAudioEndpoint, the
// IAudioSessionManager2 (un)register-notification methods, the notification
// callback interfaces, and IAudioSessionControl2::IsSystemSoundsSession.
//
// The interfaces reuse EDataFlow / AudioSessionState / IMMDevice /
// IAudioSessionEnumerator / IAudioSessionControl / NativeMethods from
// CoreAudioInterop.cs (same assembly, same namespace). As in that file, vtable
// slots that are never called are declared as empty placeholders purely to
// preserve slot order. Nothing here opens a stream or switches an endpoint.

/// <summary>Audio endpoint role (<c>ERole</c>).</summary>
internal enum ERole
{
    Console = 0,        // eConsole
    Multimedia = 1,     // eMultimedia
    Communications = 2, // eCommunications
}

/// <summary>
/// <c>IMMDeviceEnumerator</c> re-declared with the <c>GetDefaultAudioEndpoint</c>
/// slot (slot 4) that <see cref="IMMDeviceEnumerator"/> in CoreAudioInterop omits.
/// Same IID; the CLS-activated enumerator supports both via QueryInterface.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumeratorWithDefault
{
    // Slot 3 (unused here) — declared only to preserve vtable order.
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

    // Slot 4 — resolve the current default endpoint for a data-flow + role
    // (we ask for the eCommunications capture endpoint per research #26).
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
}

/// <summary>
/// <c>IAudioSessionManager2</c> re-declared with the notification (un)register
/// methods (slots 6-9) that <see cref="IAudioSessionManager2"/> in CoreAudioInterop
/// omits. Same IID; obtained by <c>IMMDevice::Activate</c> on the capture endpoint.
/// </summary>
[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2WithNotifications
{
    // IAudioSessionManager slots 3-4 (unused) — preserve vtable order.
    void GetAudioSessionControl();

    void GetSimpleAudioVolume();

    // Slot 5 — enumerate the sessions currently on this endpoint.
    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    // Slot 6 — subscribe to OnSessionCreated for newly created sessions.
    [PreserveSig]
    int RegisterSessionNotification(IAudioSessionNotification sessionNotification);

    // Slot 7.
    [PreserveSig]
    int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);

    // Slot 8 — subscribe to the OS communication-stream duck/unduck signal.
    // A null sessionId associates the notification with no specific session
    // (a pure observer), so every communication stream is reported.
    [PreserveSig]
    int RegisterDuckNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string? sessionId,
        IAudioVolumeDuckNotification duckNotification);

    // Slot 9.
    [PreserveSig]
    int UnregisterDuckNotification(IAudioVolumeDuckNotification duckNotification);
}

/// <summary>
/// <c>IAudioSessionControl2</c> — only <see cref="IsSystemSoundsSession"/> (slot 15)
/// is called; every earlier slot is a placeholder preserving vtable order. Used to
/// exclude the system-sounds session from the "mic engaged" decision (research #26).
/// </summary>
[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl slots 3-11 (unused) — preserve vtable order.
    void GetState();

    void GetDisplayName();

    void SetDisplayName();

    void GetIconPath();

    void SetIconPath();

    void GetGroupingParam();

    void SetGroupingParam();

    void RegisterAudioSessionNotification();

    void UnregisterAudioSessionNotification();

    // IAudioSessionControl2 slots 12-14 (unused) — preserve vtable order.
    void GetSessionIdentifier();

    void GetSessionInstanceIdentifier();

    void GetProcessId();

    // Slot 15 — S_OK (0) if this is the system-sounds session, S_FALSE (1) otherwise.
    [PreserveSig]
    int IsSystemSoundsSession();
}

/// <summary>
/// <c>IAudioSessionNotification</c> — implemented by the monitor to be told when a
/// new audio session is created on the endpoint (fires before the session's first
/// state change; used only as a trigger to re-reconcile, research #26 §3).
/// </summary>
[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    void OnSessionCreated(IAudioSessionControl newSession);
}

/// <summary>
/// <c>IAudioVolumeDuckNotification</c> — implemented by the monitor. The OS raises
/// these when a communication stream (render OR capture) opens/closes on the
/// eCommunications endpoint, so it also covers the Communications-render case the
/// capture-session scan cannot see (research #26 §2).
/// </summary>
[ComImport]
[Guid("C3B284D4-6D39-4359-B3CF-B56DDB3BB39C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioVolumeDuckNotification
{
    void OnVolumeDuckNotification(
        [MarshalAs(UnmanagedType.LPWStr)] string? sessionID,
        uint countCommunicationSessions);

    void OnVolumeUnduckNotification([MarshalAs(UnmanagedType.LPWStr)] string? sessionID);
}
