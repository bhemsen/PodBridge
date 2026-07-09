using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// COM interop for setting the default audio endpoint per role, isolated to the
// WindowsAudioPolicy adapter (#28). The IPolicyConfig interface is undocumented
// and reverse-engineered; every fact here (IID, CLSID, 12-method vtable order,
// SetDefaultEndpoint at slot 11, [PreserveSig] int signature, ERole values) is
// the research consensus recorded in docs/research/mic-profile-policy-ipolicyconfig.md
// (issue #25). This file only ADDS the write-path interop; the read-only
// MMDevice/property-store types it builds on live in CoreAudioInterop.cs (#23)
// and are reused unchanged. Clean-room: facts only, no GPL source copied.

/// <summary>
/// Audio endpoint role (<c>ERole</c>, mmdeviceapi.h). Values are authoritative
/// (Microsoft Learn): the Windows "Default Device" is <see cref="Console"/> +
/// <see cref="Multimedia"/>; the "Default Communication Device" is
/// <see cref="Communications"/>. They are settable independently, which is the
/// lever the mic-profile policy relies on.
/// </summary>
internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

/// <summary>Well-known coclass CLSIDs for the audio-policy write path.</summary>
internal static class PolicyConfigInterop
{
    // CPolicyConfigClient — the coclass to activate (CoCreateInstance) before
    // QueryInterface-ing for IPolicyConfig. Unanimous across the research sources.
    internal static readonly Guid PolicyConfigClsid =
        new("870af99c-171d-4f9e-af0d-e63df40c2bc9");
}

/// <summary>
/// The undocumented <c>IPolicyConfig</c> (IID <c>f8679f50-…</c>). Only
/// <see cref="SetDefaultEndpoint"/> (vtable slot 11) is called; slots 1–10 and 12
/// are declared as opaque placeholders purely to preserve vtable order so slot 11
/// resolves correctly. Twelve methods total — the 12-method layout (with
/// <c>ResetDeviceFormat</c> at slot 3) is the research majority; an 11-method
/// declaration would shift every later slot and misfire the call.
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Slots 1–10: never called; declared only to keep SetDefaultEndpoint at slot 11.
    void GetMixFormat();

    void GetDeviceFormat();

    void ResetDeviceFormat();

    void SetDeviceFormat();

    void GetProcessingPeriod();

    void SetProcessingPeriod();

    void GetShareMode();

    void SetShareMode();

    void GetPropertyValue();

    void SetPropertyValue();

    // Slot 11: the only method PodBridge calls. [PreserveSig] so a non-S_OK HRESULT
    // degrades gracefully (research decision) instead of throwing. wszDeviceId is the
    // endpoint id string from IMMDevice::GetId; one call per role.
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);

    // Slot 12: never called; declared to keep the vtable complete.
    void SetEndpointVisibility();
}

/// <summary>
/// The <c>IMMDevice</c> surface extended with <c>GetId</c> (vtable slot 3), which
/// the read-only <see cref="IMMDevice"/> in CoreAudioInterop.cs does not declare.
/// Same IID, so an <see cref="IMMDevice"/> obtained from the reused enumerator is
/// cast to this to read the endpoint id string that <c>SetDefaultEndpoint</c> needs.
/// Slots 1–2 mirror the real signatures purely to place <c>GetId</c> at slot 3.
/// </summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceWithId
{
    void Activate(
        ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    void OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
}

/// <summary>
/// The <c>IMMDeviceEnumerator</c> surface extended with <c>GetDefaultAudioEndpoint</c>
/// (vtable slot 2), which the read-only <see cref="IMMDeviceEnumerator"/> in
/// CoreAudioInterop.cs does not declare. Same IID; used only to read the endpoint
/// currently holding a given role/direction. Slot 1 mirrors <c>EnumAudioEndpoints</c>
/// to place <c>GetDefaultAudioEndpoint</c> at slot 2.
/// </summary>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumeratorWithDefault
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
}
