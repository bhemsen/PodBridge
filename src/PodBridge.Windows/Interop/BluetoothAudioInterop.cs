using System.Runtime.InteropServices;

namespace PodBridge.Windows.Interop;

// COM interop for the one-click connect / disconnect of already-paired AirPods, isolated
// to the WindowsBluetoothAudioConnector adapter. Tier 1: driver-free, admin-free. It sends
// the same one-shot request the Windows Sound control panel's "Connect" / "Disconnect"
// uses: a KS property GET on the Bluetooth audio filter behind an AirPods endpoint. Every
// IID, vtable order and constant here is from the public Windows SDK headers
// (mmdeviceapi.h, devicetopology.h, ks.h, ksmedia.h); nothing is copied from other
// projects. Only the methods actually invoked carry full signatures; earlier vtable slots
// that are never called are declared purely to preserve slot order. Reuses EDataFlow /
// IMMDevice / IMMDeviceCollection from CoreAudioInterop.cs and
// IMMDeviceEnumeratorForActivation from AudioClientInterop.cs.

/// <summary>Constants for the Bluetooth audio one-shot connect / disconnect request.</summary>
internal static class BluetoothAudioInterop
{
    // DEVICE_STATE_UNPLUGGED (mmdeviceapi.h): a paired Bluetooth audio device whose link is
    // down reports its endpoints as unplugged; connected ones are DEVICE_STATE_ACTIVE.
    internal const uint DeviceStateUnplugged = 0x00000008;

    // Enumerate only endpoints of devices that are still installed (active or unplugged),
    // so a removed / unpaired device is reported as "not found" rather than "disconnected".
    internal const uint PairedEndpointStateMask = NativeMethods.DeviceStateActive | DeviceStateUnplugged;

    // KSPROPERTY_TYPE_GET (ks.h). The one-shot requests are issued as GETs with no data.
    internal const uint KsPropertyTypeGet = 0x00000001;

    // KSPROPERTY_ONESHOT_RECONNECT / KSPROPERTY_ONESHOT_DISCONNECT (ksmedia.h,
    // KSPROPERTY_BTAUDIO enumeration): ask the Bluetooth audio driver to bring the link
    // up / down once. S_OK means the request was accepted, not that the link changed.
    internal const uint KsPropertyOneShotReconnect = 0;
    internal const uint KsPropertyOneShotDisconnect = 1;

    // Device-topology filter ids of Bluetooth audio drivers start with this prefix
    // (BTHENUM = A2DP stereo, BTHHFENUM = hands-free); other filters are ignored.
    internal const string BluetoothFilterIdPrefix = @"{2}.\\?\bth";

    // KSPROPSETID_BtAudio (ksmedia.h): the Bluetooth audio property set.
    internal static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
}

/// <summary>The <c>KSPROPERTY</c> identifier (ks.h): property set, id and flags (24 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KsProperty
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

/// <summary>
/// The <c>IMMDevice</c> surface extended through <c>GetState</c> (vtable slot 4), which the
/// read-only <see cref="IMMDevice"/> in CoreAudioInterop.cs does not declare. Same IID;
/// slots 1–3 mirror the real signatures purely to place <c>GetState</c> at slot 4.
/// </summary>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceWithState
{
    void Activate(
        ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    void OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    void GetState(out uint pdwState);
}

/// <summary><c>IDeviceTopology</c> (devicetopology.h) — the endpoint's device topology.</summary>
[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    void GetConnectorCount(out uint pCount);

    void GetConnector(uint nIndex, out IConnector ppConnector);

    // Slots 2–4 (unused) — declared only to place GetDeviceId at slot 5.
    void GetSubunitCount(out uint pCount);

    void GetSubunit(uint nIndex, out IntPtr ppSubunit);

    void GetPartById(uint nId, out IntPtr ppPart);

    void GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId);
}

/// <summary><c>IConnector</c> (devicetopology.h) — a connection point between two topologies.</summary>
[ComImport]
[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    // Slots 0–4 (unused) — declared only to place GetConnectedTo at slot 5.
    void GetConnectorType(out int pType);

    void GetDataFlow(out int pFlow);

    void ConnectTo(IntPtr pConnectTo);

    void Disconnect();

    void IsConnected(out int pbConnected);

    // An unconnected connector returns E_NOTFOUND; PreserveSig lets the caller skip it.
    [PreserveSig]
    int GetConnectedTo(out IConnector ppConTo);
}

/// <summary>
/// <c>IPart</c> (devicetopology.h) — queried from the connected <see cref="IConnector"/> to
/// reach the device topology (the driver's KS filter) on the other side of the endpoint.
/// </summary>
[ComImport]
[Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPart
{
    // Slots 0–8 (unused) — declared only to place GetTopologyObject at slot 9.
    void GetName(out IntPtr ppwstrName);

    void GetLocalId(out uint pnId);

    void GetGlobalId(out IntPtr ppwstrGlobalId);

    void GetPartType(out int pPartType);

    void GetSubType(out Guid pSubType);

    void GetControlInterfaceCount(out uint pCount);

    void GetControlInterface(uint nIndex, out IntPtr ppInterfaceDesc);

    void EnumPartsIncoming(out IntPtr ppParts);

    void EnumPartsOutgoing(out IntPtr ppParts);

    void GetTopologyObject(out IDeviceTopology ppTopology);
}

/// <summary><c>IKsControl</c> (ks.h) — sends a KS property request to a driver filter.</summary>
[ComImport]
[Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig]
    int KsProperty(
        ref KsProperty property,
        uint propertyLength,
        IntPtr propertyData,
        uint dataLength,
        out uint bytesReturned);
}
