/*++

Module: Driver.h

    Shared declarations for the PodBridge AAP KMDF L2CAP bridge: the per-device
    context, the pool tag, and the cross-module function prototypes.

    Clean-room implementation from the documented mechanics in research #40
    (Bluetooth profile-driver I/O model, BRB flow, WDF device interface).

--*/

#pragma once

#include <ntddk.h>
#include <wdf.h>

//
// Bluetooth headers. Order matters: bthdef.h/bthioctl.h (Windows SDK, shared)
// define BTH_ADDR / BTH_DEVICE_INFO / the internal BTHENUM + SUBMIT_BRB IOCTLs
// that bthddi.h (WDK, km) relies on. bthguid.h provides the profile-driver
// interface GUID. GUID storage is emitted in Device.c (which defines INITGUID).
//
#include <bthdef.h>
#include <bthioctl.h>
#include <bthddi.h>
#include <bthguid.h>

#include "Public.h"

//
// Pool tag ("PdgB" reads as PodBridge in the debugger's little-endian dump).
//
#define PODBRIDGE_POOL_TAG  'BgdP'

//
// L2CAP MTU range offered on the open BRB. Kept wide (min = the L2CAP floor)
// so channel negotiation with the AirPods is not constrained -- research #40
// warns against raising the minimum MTU (negotiation can then fail).
//
#define PODBRIDGE_L2CAP_MIN_MTU        ((USHORT)48)
#define PODBRIDGE_L2CAP_PREFERRED_MTU  ((USHORT)672)
#define PODBRIDGE_L2CAP_MAX_MTU        ((USHORT)0xFFFF)

//
// Depth of the port driver's inbound L2CAP queue (research #40 recommends 10).
//
#define PODBRIDGE_INCOMING_QUEUE_DEPTH ((UCHAR)10)

//
// Per-device context. One instance lives on the FDO that BthEnum's PDO is the
// parent of; it holds everything needed to run the single AAP L2CAP channel.
//
typedef struct _PODBRIDGE_DEVICE_CONTEXT
{
    WDFDEVICE Device;

    //
    // Local (lower) I/O target: IRPs sent here go down to Bthport via the
    // BthEnum PDO. Every BRB and the GET_DEVINFO query is submitted on it.
    //
    WDFIOTARGET IoTarget;

    //
    // Profile-driver interface obtained by QUERY_INTERFACE: the BthAllocateBrb /
    // BthFreeBrb / BthInitializeBrb / BthReuseBrb helpers used for every BRB.
    //
    BTH_PROFILE_DRIVER_INTERFACE ProfileInterface;

    //
    // Remote AirPods address learned from IOCTL_INTERNAL_BTHENUM_GET_DEVINFO;
    // used as BRB_L2CA_OPEN_CHANNEL.BtAddress. TRUE once both the interface and
    // the address have been acquired in EvtDevicePrepareHardware.
    //
    BTH_ADDR RemoteAddress;
    BOOLEAN HardwareReady;

    //
    // Passive-level lock serializing connect/disconnect between the IOCTL path
    // and the PnP release path (both run at PASSIVE_LEVEL and may block while a
    // BRB is submitted, so a sleepable wait-lock -- not a spin lock -- is used).
    //
    WDFWAITLOCK ConnectionLock;
    L2CAP_CHANNEL_HANDLE ChannelHandle;
    BOOLEAN Connected;

    //
    // Manual queue holding the pending inverted-call RECEIVE IOCTLs.
    //
    WDFQUEUE ReceiveQueue;

    //
    // Dedicated receive thread: loops issuing inbound L2CAP read BRBs and hands
    // each frame to a parked RECEIVE request. StopRequested + closing the
    // channel (which aborts the outstanding read) tear it down cleanly.
    //
    PKTHREAD ReceiveThread;
    volatile LONG StopRequested;

} PODBRIDGE_DEVICE_CONTEXT, *PPODBRIDGE_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(PODBRIDGE_DEVICE_CONTEXT, PodBridgeGetDeviceContext)

//
// Driver.c
//
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD PodBridgeEvtDeviceAdd;

//
// Device.c
//
EVT_WDF_DEVICE_PREPARE_HARDWARE PodBridgeEvtDevicePrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE PodBridgeEvtDeviceReleaseHardware;

//
// Queue.c
//
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL PodBridgeEvtIoDeviceControl;

_IRQL_requires_max_(DISPATCH_LEVEL)
VOID
PodBridgeDeliverInboundFrame(
    _In_ PPODBRIDGE_DEVICE_CONTEXT DeviceContext,
    _In_reads_bytes_(FrameLength) PVOID Frame,
    _In_ ULONG FrameLength);

//
// L2cap.c
//
_IRQL_requires_max_(PASSIVE_LEVEL)
NTSTATUS
PodBridgeConnectChannel(_In_ PPODBRIDGE_DEVICE_CONTEXT DeviceContext);

_IRQL_requires_max_(PASSIVE_LEVEL)
VOID
PodBridgeDisconnectChannel(_In_ PPODBRIDGE_DEVICE_CONTEXT DeviceContext);

_IRQL_requires_max_(PASSIVE_LEVEL)
NTSTATUS
PodBridgeSendFrame(
    _In_ PPODBRIDGE_DEVICE_CONTEXT DeviceContext,
    _In_reads_bytes_(FrameLength) PVOID Frame,
    _In_ ULONG FrameLength);
