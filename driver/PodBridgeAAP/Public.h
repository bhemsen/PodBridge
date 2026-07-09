/*++

Module: Public.h

    PodBridge AAP KMDF L2CAP bridge -- public user-mode/kernel contract.

    This header is the single source of truth for the interface GUID, the
    Bluetooth service GUID used by the INF hardware-id match, and the IOCTL
    contract that the user-mode DriverAapTransport (issue #43, PodBridge.Windows)
    uses to reach the driver. It is safe to include from both kernel-mode
    (driver) and user-mode (transport) code.

    Clean-room: implemented in our own words from the documented KMDF/Bluetooth
    mechanics recorded in research issue #40. No source copied from any project.

--*/

#pragma once

//
// Device interface GUID exposed via WdfDeviceCreateDeviceInterface. The
// user-mode transport enumerates this with CM_Get_Device_Interface_List /
// SetupDiGetClassDevs and opens it with CreateFile. Driver-present == interface
// present, which is exactly the "probe -> Unavailable when absent" check the
// spec requires (docs/specs/spec-advanced-driver-anc.md). New project GUID.
//
// {A5FD3D2B-12A0-40AF-AE19-EF110C488DFF}
DEFINE_GUID(GUID_DEVINTERFACE_PODBRIDGE_AAP,
    0xa5fd3d2b, 0x12a0, 0x40af, 0xae, 0x19, 0xef, 0x11, 0x0c, 0x48, 0x8d, 0xff);

//
// PodBridge AAP Bluetooth service GUID. BthEnum expands the service GUID a
// remote device advertises into a hardware id of the form
// BTHENUM\{ServiceGUID}; the INF [Models] section binds to that id (research
// #40 (c)). This is a custom PodBridge service GUID (not a SIG 16-bit value).
//
// {36F88597-6BAE-4E3D-A454-66C3D877F4EA}
DEFINE_GUID(GUID_PODBRIDGE_AAP_SERVICE,
    0x36f88597, 0x6bae, 0x4e3d, 0xa4, 0x54, 0x66, 0xc3, 0xd8, 0x77, 0xf4, 0xea);

//
// AAP control-channel PSM. Documented fact (research #40 / prior-art): the AAP
// control channel is Bluetooth-Classic L2CAP PSM 0x1001 (fixed; no SDP lookup
// needed for a client connect). Only the cleartext control channel is used --
// MagicPairing encryption is never touched (constitution).
//
#define PODBRIDGE_AAP_PSM  ((USHORT)0x1001)

//
// Largest single AAP frame accepted on SEND / delivered on RECEIVE. AAP control
// frames are small; this bounds the user-mode buffers and the driver's internal
// receive buffer.
//
#define PODBRIDGE_MAX_FRAME  ((ULONG)1024)

//
// IOCTL contract (research #40 (b)). METHOD_BUFFERED so the framework copies the
// small AAP frames to/from a system buffer; access rights gate open handles.
//
//   IOCTL_PODBRIDGE_CONNECT  -- open the L2CAP channel to the connected AirPods
//                               (idempotent; completes once the open BRB does).
//   IOCTL_PODBRIDGE_SEND     -- input buffer is exactly one AAP frame to write.
//   IOCTL_PODBRIDGE_RECEIVE  -- inverted call: the caller posts this ahead of
//                               time; the driver parks it on a manual queue and
//                               completes it with the next inbound AAP frame.
//
#define IOCTL_PODBRIDGE_CONNECT \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_READ_DATA | FILE_WRITE_DATA)

#define IOCTL_PODBRIDGE_SEND \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_WRITE_DATA)

#define IOCTL_PODBRIDGE_RECEIVE \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_READ_DATA)
