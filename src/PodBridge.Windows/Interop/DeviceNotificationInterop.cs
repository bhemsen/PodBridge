using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// Notification-capable Core Audio COM interop used by WindowsAudioEndpointChangeMonitor
// to observe device-topology changes (endpoint added / removed / default changed)
// driver-free and admin-free, so the mic-profile policy can Refresh() its degrade
// warning live. It complements CoreAudioInterop.cs (read-only, intentionally NOT edited
// here) by declaring the IMMDeviceEnumerator vtable slots that file omits — the
// endpoint-notification (un)register methods (slots 6-7) — plus the IMMNotificationClient
// callback interface. EDataFlow / IMMDevice / IMMDeviceCollection / PropertyKey /
// NativeMethods are REUSED from CoreAudioInterop.cs and ERole from PolicyConfigInterop.cs
// (same assembly, same namespace) — not re-declared, to avoid duplicate-type collisions.
// As in those files, vtable slots that are never called are declared as empty
// placeholders purely to preserve slot order. Nothing here opens a stream or switches an
// endpoint — it only registers/unregisters a notification callback.

/// <summary>
/// <c>IMMDeviceEnumerator</c> re-declared with the endpoint-notification (un)register
/// methods (slots 6-7) that <see cref="IMMDeviceEnumerator"/> in CoreAudioInterop omits.
/// Same IID; slots 3-5 mirror the real signatures purely to place
/// <c>RegisterEndpointNotificationCallback</c> at slot 6.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumeratorWithCallback
{
    // Slot 3.
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

    // Slot 4.
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

    // Slot 5 — never called; declared only to keep the (un)register methods aligned.
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

    // Slot 6 — subscribe to endpoint-topology change notifications.
    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    // Slot 7.
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

/// <summary>
/// <c>IMMNotificationClient</c> — implemented by the monitor to be told when an audio
/// endpoint is added, removed, its state changes, or a default device changes. Each
/// method returns <c>S_OK</c> (a managed <c>void</c> maps to it). Callbacks arrive on an
/// OS background thread and must not block or re-enter the enumerator.
/// </summary>
[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);

    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

    void OnDefaultDeviceChanged(
        EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? pwstrDefaultDeviceId);

    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
}
