using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// Minimal, read-only Core Audio (MMDevice / WASAPI session) COM interop used by
// WindowsAudioStateReader to detect the active A2DP-vs-HFP mic mode driver-free
// and admin-free per docs/research/mic-mode-detection.md (#21). All types are
// internal (not part of the public API). Only the methods actually invoked are
// declared with full signatures; earlier vtable slots that are never called are
// declared as empty placeholders purely to preserve slot order. Nothing here
// switches an endpoint or opens a stream — see WindowsAudioStateReader.

/// <summary>Audio data-flow direction (<c>EDataFlow</c>).</summary>
internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

/// <summary>Audio session state (<c>AudioSessionState</c>).</summary>
internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

/// <summary>The <c>PROPERTYKEY</c> (format id + property id) identifying a device property.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

#pragma warning disable CS0649 // Fields are populated by native COM marshaling, not managed code.
/// <summary>
/// The <c>PROPVARIANT</c> union header. The reserved fields and the trailing
/// pointer mirror the native layout so the marshaled size is correct (24 bytes on
/// x64, 16 on x86) and <c>IPropertyStore::GetValue</c> cannot overrun the buffer.
/// Only <see cref="Vt"/> and <see cref="DataPtr"/> are read (VT_LPWSTR / VT_CLSID).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr DataPtr;
    public IntPtr DataPtr2;
}
#pragma warning restore CS0649

/// <summary>Well-known endpoint property keys.</summary>
internal static class PropertyKeys
{
    // PKEY_Device_FriendlyName — the full endpoint name, e.g. "Headphones (AirPods Pro)".
    internal static readonly PropertyKey DeviceFriendlyName =
        new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    // PKEY_Device_ContainerId — endpoints of the same physical device share this GUID;
    // used to pair the AirPods render and capture endpoints (research #21, source 4).
    internal static readonly PropertyKey DeviceContainerId =
        new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
}

/// <summary>Native helpers and constants for the read-only Core Audio path.</summary>
internal static class NativeMethods
{
    // CLSID_MMDeviceEnumerator — activated via Type.GetTypeFromCLSID + Activator, then
    // Queried for IMMDeviceEnumerator (the object→interface cast is a COM QueryInterface).
    internal static readonly Guid MMDeviceEnumeratorClsid =
        new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    internal const uint DeviceStateActive = 0x00000001; // DEVICE_STATE_ACTIVE
    internal const uint StgmRead = 0x00000000;          // STGM_READ
    internal const uint ClsCtxAll = 0x00000017;         // CLSCTX_ALL

    private const ushort VtLpwstr = 31; // VT_LPWSTR
    private const ushort VtClsid = 72;  // VT_CLSID

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int PropVariantClear(ref PropVariant pvar);

    /// <summary>Reads a VT_LPWSTR property; returns null for any other type. Always frees the PROPVARIANT.</summary>
    internal static string? GetStringProperty(IPropertyStore store, PropertyKey key)
    {
        var pv = default(PropVariant);
        try
        {
            store.GetValue(ref key, out pv);
            return pv.Vt == VtLpwstr ? Marshal.PtrToStringUni(pv.DataPtr) : null;
        }
        finally
        {
            _ = PropVariantClear(ref pv);
        }
    }

    /// <summary>Reads a VT_CLSID property; returns null for any other type. Always frees the PROPVARIANT.</summary>
    internal static Guid? GetGuidProperty(IPropertyStore store, PropertyKey key)
    {
        var pv = default(PropVariant);
        try
        {
            store.GetValue(ref key, out pv);
            if (pv.Vt == VtClsid && pv.DataPtr != IntPtr.Zero)
            {
                return Marshal.PtrToStructure<Guid>(pv.DataPtr);
            }

            return null;
        }
        finally
        {
            _ = PropVariantClear(ref pv);
        }
    }
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint pcDevices);

    void Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(
        ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    void OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);

    void GetAt(uint iProp, out PropertyKey pkey);

    void GetValue(ref PropertyKey key, out PropVariant pv);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager slot 0 (unused) — declared only to preserve vtable order.
    void GetAudioSessionControl();

    // IAudioSessionManager slot 1 (unused).
    void GetSimpleAudioVolume();

    void GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int sessionCount);

    void GetSession(int sessionIndex, out IAudioSessionControl session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    void GetState(out AudioSessionState state);
}
