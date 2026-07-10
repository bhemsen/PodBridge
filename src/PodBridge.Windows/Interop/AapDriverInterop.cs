using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PodBridge.Windows.Interop;

// Win32 seam for the KMDF AAP L2CAP bridge driver (driver/PodBridgeAAP, issue #41).
// DriverAapTransport (issue #43) reaches the driver ONLY through the two internal
// interfaces below; the concrete Win32 pair does the real P/Invoke and the fakes in
// PodBridge.Windows.Tests substitute at this seam so the transport's connect / send /
// receive-loop / graceful-absence logic is device-independent (the real driver is a
// human smoke test — CI has no hardware). Every constant and IOCTL mirrors the driver's
// single source of truth, driver/PodBridgeAAP/Public.h (clean-room: our own words, no
// source copied). Nothing here interprets AAP bytes — that stays in Core's AapProtocol.

/// <summary>
/// Probes for the driver's device interface and opens a channel to it. Presence of the
/// interface == driver installed, which is exactly the "report Unavailable when absent"
/// check the spec (docs/specs/spec-advanced-driver-anc.md) requires.
/// </summary>
internal interface IAapDriverInterop
{
    /// <summary>
    /// Enumerates the PodBridge AAP device interface. Returns <see langword="false"/>
    /// with a <see langword="null"/> path when the driver is absent (Tier-1 default);
    /// never throws for the absent case.
    /// </summary>
    bool TryFindInterfacePath(out string? interfacePath);

    /// <summary>Opens the device interface at <paramref name="interfacePath"/> (CreateFile).</summary>
    IAapDriverChannel Open(string interfacePath);
}

/// <summary>
/// One logical channel to the driver's device interface: the connect / send IOCTLs and the
/// blocking inverted-call receive IOCTL. <see cref="Receive"/> parks on the driver's
/// manual queue until a frame arrives (or the request is cancelled / the handle closes),
/// so a background loop can surface inbound AAP frames without polling. The Win32
/// implementation backs this with a dedicated handle for <see cref="Receive"/> so the
/// parked request cannot serialize <see cref="Send"/> (see <see cref="Win32AapDriverInterop.Open"/>).
/// </summary>
internal interface IAapDriverChannel : IDisposable
{
    /// <summary>Opens the L2CAP channel to the connected AirPods (IOCTL_PODBRIDGE_CONNECT).</summary>
    void Connect();

    /// <summary>Writes one raw AAP frame to the channel (IOCTL_PODBRIDGE_SEND).</summary>
    void Send(ReadOnlyMemory<byte> frame);

    /// <summary>
    /// Blocks on the pending receive IOCTL (IOCTL_PODBRIDGE_RECEIVE) and returns the byte
    /// count of the next inbound frame written into <paramref name="buffer"/>, or
    /// <c>0</c> when the request was cancelled or the channel closed (loop-stop signal).
    /// </summary>
    int Receive(byte[] buffer);

    /// <summary>Cancels a blocked <see cref="Receive"/> so the receive loop can stop (CancelIoEx).</summary>
    void CancelPendingReceive();
}

/// <summary>Real Win32 implementation of <see cref="IAapDriverInterop"/> over the KMDF driver.</summary>
internal sealed class Win32AapDriverInterop : IAapDriverInterop
{
    public bool TryFindInterfacePath(out string? interfacePath)
    {
        interfacePath = null;
        var guid = AapDriverNativeMethods.InterfaceGuid;

        // Size-then-fill on the PRESENT set (flag 0): an empty list == driver not installed.
        if (AapDriverNativeMethods.CM_Get_Device_Interface_List_SizeW(
                out var length, ref guid, IntPtr.Zero, 0) != AapDriverNativeMethods.CrSuccess
            || length < 2)
        {
            return false;
        }

        var buffer = new char[length];
        if (AapDriverNativeMethods.CM_Get_Device_Interface_ListW(
                ref guid, IntPtr.Zero, buffer, length, 0) != AapDriverNativeMethods.CrSuccess)
        {
            return false;
        }

        // The result is a double-null-terminated multi-string; take the first entry.
        var first = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        interfacePath = string.IsNullOrEmpty(first) ? null : first;
        return interfacePath is not null;
    }

    public IAapDriverChannel Open(string interfacePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(interfacePath);

        // Open the interface TWICE: one handle for connect/send, a separate one for the
        // receive loop. The inverted-call RECEIVE IOCTL parks indefinitely (research #40
        // (b)), and a Windows *synchronous* file object serializes every request on it
        // (FO_SYNCHRONOUS_IO). If SEND shared that handle it would block behind the parked
        // RECEIVE, the AirPods would never get the command, no echo would arrive, and the
        // ANC round-trip would deadlock. A second file object lets SEND dispatch while
        // RECEIVE is parked. This is safe because the driver keeps the L2CAP channel and
        // its Connected flag / manual RECEIVE queue in its DEVICE context, not per file
        // object (driver/PodBridgeAAP/Device.c + Queue.c), so both handles drive the one
        // shared channel; the interface is opened shared (not exclusive) so a second open
        // succeeds. Cancellation stays CancelIoEx on the receive handle (unchanged).
        var controlHandle = OpenInterfaceHandle(interfacePath);
        SafeFileHandle receiveHandle;
        try
        {
            receiveHandle = OpenInterfaceHandle(interfacePath);
        }
        catch
        {
            controlHandle.Dispose();
            throw;
        }

        return new Win32AapDriverChannel(controlHandle, receiveHandle);
    }

    private static SafeFileHandle OpenInterfaceHandle(string interfacePath)
    {
        var handle = AapDriverNativeMethods.CreateFileW(
            interfacePath,
            AapDriverNativeMethods.GenericRead | AapDriverNativeMethods.GenericWrite,
            AapDriverNativeMethods.FileShareRead | AapDriverNativeMethods.FileShareWrite,
            IntPtr.Zero,
            AapDriverNativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return handle;
    }
}

/// <summary>
/// Real Win32 channel: DeviceIoControl over the device interface. Holds TWO handles to the
/// same interface — <c>_controlHandle</c> for connect/send and a dedicated <c>_receiveHandle</c>
/// for the parked inverted-call RECEIVE — so the permanently-blocked RECEIVE never serializes
/// SEND on one synchronous file object (see <see cref="Win32AapDriverInterop.Open"/> for why).
/// </summary>
internal sealed class Win32AapDriverChannel : IAapDriverChannel
{
    private readonly SafeFileHandle _controlHandle;
    private readonly SafeFileHandle _receiveHandle;

    internal Win32AapDriverChannel(SafeFileHandle controlHandle, SafeFileHandle receiveHandle)
    {
        _controlHandle = controlHandle;
        _receiveHandle = receiveHandle;
    }

    public void Connect() => Control(AapDriverNativeMethods.IoctlConnect, null, 0, null, 0);

    public void Send(ReadOnlyMemory<byte> frame)
    {
        var bytes = frame.ToArray();
        Control(AapDriverNativeMethods.IoctlSend, bytes, (uint)bytes.Length, null, 0);
    }

    public int Receive(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        // Blocks on the RECEIVE IOCTL on the dedicated receive handle until an inbound frame
        // arrives. A failure here is the normal stop signal (CancelPendingReceive / handle
        // closed), so it returns 0 rather than throwing — the loop treats 0 as "channel
        // gone" and exits cleanly.
        if (!AapDriverNativeMethods.DeviceIoControl(
                _receiveHandle, AapDriverNativeMethods.IoctlReceive, null, 0,
                buffer, (uint)buffer.Length, out var returned, IntPtr.Zero))
        {
            return 0;
        }

        return (int)returned;
    }

    public void CancelPendingReceive()
    {
        if (!_receiveHandle.IsInvalid && !_receiveHandle.IsClosed)
        {
            _ = AapDriverNativeMethods.CancelIoEx(_receiveHandle, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        _controlHandle.Dispose();
        _receiveHandle.Dispose();
    }

    private void Control(uint code, byte[]? input, uint inputLength, byte[]? output, uint outputLength)
    {
        if (!AapDriverNativeMethods.DeviceIoControl(
                _controlHandle, code, input, inputLength, output, outputLength, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}

/// <summary>
/// P/Invoke surface and constants for the AAP driver's user-mode contract. GUID and IOCTLs
/// are the C# transcription of driver/PodBridgeAAP/Public.h — the single source of truth
/// shared by both sides (clean-room: restated, not copied).
/// </summary>
internal static class AapDriverNativeMethods
{
    // GUID_DEVINTERFACE_PODBRIDGE_AAP {A5FD3D2B-12A0-40AF-AE19-EF110C488DFF} (Public.h).
    internal static readonly Guid InterfaceGuid = new("A5FD3D2B-12A0-40AF-AE19-EF110C488DFF");

    // Largest single AAP frame (PODBRIDGE_MAX_FRAME in Public.h) — bounds the receive buffer.
    internal const int MaxFrameLength = 1024;

    internal const int CrSuccess = 0; // CR_SUCCESS from CONFIGRET.

    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;

    // CTL_CODE(FILE_DEVICE_UNKNOWN, function, METHOD_BUFFERED, access) — values identical to
    // the IOCTL_PODBRIDGE_* macros in Public.h (0x800/0x801/0x802).
    internal static readonly uint IoctlConnect =
        CtlCode(FileDeviceUnknown, 0x800, MethodBuffered, FileReadData | FileWriteData);

    internal static readonly uint IoctlSend =
        CtlCode(FileDeviceUnknown, 0x801, MethodBuffered, FileWriteData);

    internal static readonly uint IoctlReceive =
        CtlCode(FileDeviceUnknown, 0x802, MethodBuffered, FileReadData);

    private const uint FileDeviceUnknown = 0x00000022;
    private const uint MethodBuffered = 0;
    private const uint FileReadData = 0x0001;
    private const uint FileWriteData = 0x0002;

    private static uint CtlCode(uint deviceType, uint function, uint method, uint access)
        => (deviceType << 16) | (access << 14) | (function << 2) | method;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CM_Get_Device_Interface_List_SizeW(
        out uint pulLen, ref Guid interfaceClassGuid, IntPtr pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, IntPtr pDeviceID, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, byte[]? lpInBuffer, uint nInBufferSize,
        byte[]? lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);
}
