/*++

Module: L2cap.c

    The Bluetooth-Classic L2CAP half of the PodBridge AAP bridge: open the AAP
    control channel (PSM 0x1001) to the AirPods, send AAP frames, run a receive
    thread that pulls inbound frames, and close the channel on teardown.

    All BRBs are allocated via the profile-driver interface helpers and submitted
    to Bthport with IOCTL_INTERNAL_BTH_SUBMIT_BRB (the BRB pointer travels in the
    IRP's Parameters.Others.Argument1, which is what the "InternalIoctlOthers"
    WDF helper sets). Research #40 (a): open/send/close BRB flow; cleartext
    control channel only -- encryption is never forced (constitution).

    Clean-room; no source copied from any project.

--*/

#include "Driver.h"

static KSTART_ROUTINE PodBridgeReceiveThreadRoutine;

//
// Submit one already-filled BRB synchronously (PASSIVE_LEVEL). The BRB pointer
// is passed as OtherArg1, landing in Parameters.Others.Argument1 for Bthport.
//
static NTSTATUS
PodBridgeSubmitBrbSync(
    _In_ WDFIOTARGET IoTarget,
    _In_ PBRB Brb,
    _In_ size_t BrbSize)
{
    WDF_MEMORY_DESCRIPTOR descriptor;

    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&descriptor, Brb, (ULONG)BrbSize);

    return WdfIoTargetSendInternalIoctlOthersSynchronously(
        IoTarget,
        NULL,
        IOCTL_INTERNAL_BTH_SUBMIT_BRB,
        &descriptor,
        NULL,
        NULL,
        NULL,
        NULL);
}

//
// Registered on the open channel for CALLBACK_DISCONNECT. On a remote drop we
// only flag the receive loop to stop; the actual teardown happens at PASSIVE.
// Runs at <= DISPATCH_LEVEL, so it does nothing that could block.
//
static void
PodBridgeIndicationCallback(
    _In_opt_ PVOID Context,
    _In_ INDICATION_CODE Indication,
    _In_ PINDICATION_PARAMETERS Parameters)
{
    PPODBRIDGE_DEVICE_CONTEXT context = (PPODBRIDGE_DEVICE_CONTEXT)Context;

    UNREFERENCED_PARAMETER(Parameters);

    if (context != NULL && Indication == IndicationRemoteDisconnect) {
        InterlockedExchange(&context->StopRequested, 1);
    }
}

//
// Build and submit BRB_L2CA_OPEN_CHANNEL for PSM 0x1001 to the AirPods address.
// On success the negotiated channel handle is stored in the context.
//
static NTSTATUS
PodBridgeOpenChannel(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    struct _BRB_L2CA_OPEN_CHANNEL *brb;
    NTSTATUS status;

    brb = (struct _BRB_L2CA_OPEN_CHANNEL *)
        Context->ProfileInterface.BthAllocateBrb(BRB_L2CA_OPEN_CHANNEL, PODBRIDGE_POOL_TAG);
    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb->BtAddress = Context->RemoteAddress;
    brb->Psm = PODBRIDGE_AAP_PSM;
    brb->ChannelFlags = CF_ROLE_EITHER;   // no CF_LINK_ENCRYPTED: cleartext AAP.

    brb->ConfigOut.Mtu.Min = PODBRIDGE_L2CAP_MIN_MTU;
    brb->ConfigOut.Mtu.Preferred = PODBRIDGE_L2CAP_PREFERRED_MTU;
    brb->ConfigOut.Mtu.Max = PODBRIDGE_L2CAP_MAX_MTU;
    brb->ConfigIn.Mtu.Min = PODBRIDGE_L2CAP_MIN_MTU;
    brb->ConfigIn.Mtu.Preferred = PODBRIDGE_L2CAP_PREFERRED_MTU;
    brb->ConfigIn.Mtu.Max = PODBRIDGE_L2CAP_MAX_MTU;

    brb->CallbackFlags = CALLBACK_DISCONNECT;
    brb->Callback = PodBridgeIndicationCallback;
    brb->CallbackContext = Context;
    brb->ReferenceObject = WdfDeviceWdmGetDeviceObject(Context->Device);
    brb->IncomingQueueDepth = PODBRIDGE_INCOMING_QUEUE_DEPTH;

    status = PodBridgeSubmitBrbSync(Context->IoTarget, (PBRB)brb, sizeof(*brb));
    if (NT_SUCCESS(status)) {
        Context->ChannelHandle = brb->ChannelHandle;
    }

    Context->ProfileInterface.BthFreeBrb((PBRB)brb);
    return status;
}

//
// Close the open channel. Caller holds ConnectionLock.
//
static VOID
PodBridgeCloseChannelLocked(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    struct _BRB_L2CA_CLOSE_CHANNEL *brb;

    if (Context->ChannelHandle == NULL) {
        return;
    }

    brb = (struct _BRB_L2CA_CLOSE_CHANNEL *)
        Context->ProfileInterface.BthAllocateBrb(BRB_L2CA_CLOSE_CHANNEL, PODBRIDGE_POOL_TAG);
    if (brb != NULL) {
        brb->BtAddress = Context->RemoteAddress;
        brb->ChannelHandle = Context->ChannelHandle;
        (VOID)PodBridgeSubmitBrbSync(Context->IoTarget, (PBRB)brb, sizeof(*brb));
        Context->ProfileInterface.BthFreeBrb((PBRB)brb);
    }
    Context->ChannelHandle = NULL;
}

//
// Receive loop: keep an inbound L2CAP read BRB posted; each completed read is a
// received AAP frame handed to a parked RECEIVE request. The blocking read is
// aborted when the channel closes, which is how the loop is unblocked on stop.
//
static VOID
PodBridgeReceiveThreadRoutine(
    _In_ PVOID StartContext)
{
    PPODBRIDGE_DEVICE_CONTEXT context = (PPODBRIDGE_DEVICE_CONTEXT)StartContext;
    struct _BRB_L2CA_ACL_TRANSFER *brb;
    PVOID buffer;
    NTSTATUS status;

    buffer = ExAllocatePool2(POOL_FLAG_NON_PAGED, PODBRIDGE_MAX_FRAME, PODBRIDGE_POOL_TAG);
    brb = (struct _BRB_L2CA_ACL_TRANSFER *)
        context->ProfileInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, PODBRIDGE_POOL_TAG);
    if (buffer == NULL || brb == NULL) {
        goto cleanup;
    }

    while (context->StopRequested == 0) {
        context->ProfileInterface.BthReuseBrb((PBRB)brb, BRB_L2CA_ACL_TRANSFER);
        brb->BtAddress = context->RemoteAddress;
        brb->ChannelHandle = context->ChannelHandle;
        brb->TransferFlags = ACL_TRANSFER_DIRECTION_IN | ACL_SHORT_TRANSFER_OK;
        brb->BufferSize = PODBRIDGE_MAX_FRAME;
        brb->Buffer = buffer;
        brb->BufferMDL = NULL;

        status = PodBridgeSubmitBrbSync(context->IoTarget, (PBRB)brb, sizeof(*brb));
        if (!NT_SUCCESS(status)) {
            break;   // channel closing / link gone -> stop the loop.
        }
        if (brb->BufferSize > 0) {
            PodBridgeDeliverInboundFrame(context, buffer, brb->BufferSize);
        }
    }

cleanup:
    if (brb != NULL) {
        context->ProfileInterface.BthFreeBrb((PBRB)brb);
    }
    if (buffer != NULL) {
        ExFreePoolWithTag(buffer, PODBRIDGE_POOL_TAG);
    }
    PsTerminateSystemThread(STATUS_SUCCESS);
}

static NTSTATUS
PodBridgeStartReceiveThread(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    HANDLE threadHandle;
    NTSTATUS status;

    Context->StopRequested = 0;

    status = PsCreateSystemThread(
        &threadHandle,
        THREAD_ALL_ACCESS,
        NULL,
        NULL,
        NULL,
        PodBridgeReceiveThreadRoutine,
        Context);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = ObReferenceObjectByHandle(
        threadHandle,
        THREAD_ALL_ACCESS,
        *PsThreadType,
        KernelMode,
        (PVOID *)&Context->ReceiveThread,
        NULL);
    ZwClose(threadHandle);
    return status;
}

static VOID
PodBridgeStopReceiveThread(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    if (Context->ReceiveThread != NULL) {
        KeWaitForSingleObject(Context->ReceiveThread, Executive, KernelMode, FALSE, NULL);
        ObDereferenceObject(Context->ReceiveThread);
        Context->ReceiveThread = NULL;
    }
}

//
// Complete any RECEIVE requests still parked once the channel is gone, so the
// user-mode transport's outstanding inverted call does not hang.
//
static VOID
PodBridgeFlushReceiveQueue(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    WDFREQUEST request;

    while (NT_SUCCESS(WdfIoQueueRetrieveNextRequest(Context->ReceiveQueue, &request))) {
        WdfRequestComplete(request, STATUS_DEVICE_NOT_CONNECTED);
    }
}

_Use_decl_annotations_
NTSTATUS
PodBridgeConnectChannel(
    PPODBRIDGE_DEVICE_CONTEXT DeviceContext)
{
    NTSTATUS status;

    if (!DeviceContext->HardwareReady) {
        return STATUS_DEVICE_NOT_READY;
    }

    WdfWaitLockAcquire(DeviceContext->ConnectionLock, NULL);
    if (DeviceContext->Connected) {
        WdfWaitLockRelease(DeviceContext->ConnectionLock);
        return STATUS_SUCCESS;   // idempotent.
    }

    status = PodBridgeOpenChannel(DeviceContext);
    if (NT_SUCCESS(status)) {
        status = PodBridgeStartReceiveThread(DeviceContext);
        if (NT_SUCCESS(status)) {
            DeviceContext->Connected = TRUE;
        } else {
            PodBridgeCloseChannelLocked(DeviceContext);
        }
    }
    WdfWaitLockRelease(DeviceContext->ConnectionLock);
    return status;
}

_Use_decl_annotations_
VOID
PodBridgeDisconnectChannel(
    PPODBRIDGE_DEVICE_CONTEXT DeviceContext)
{
    WdfWaitLockAcquire(DeviceContext->ConnectionLock, NULL);
    if (DeviceContext->Connected) {
        InterlockedExchange(&DeviceContext->StopRequested, 1);
        PodBridgeCloseChannelLocked(DeviceContext);   // aborts the pending read.
        PodBridgeStopReceiveThread(DeviceContext);
        DeviceContext->Connected = FALSE;
    }
    WdfWaitLockRelease(DeviceContext->ConnectionLock);

    PodBridgeFlushReceiveQueue(DeviceContext);
}

_Use_decl_annotations_
NTSTATUS
PodBridgeSendFrame(
    PPODBRIDGE_DEVICE_CONTEXT DeviceContext,
    PVOID Frame,
    ULONG FrameLength)
{
    struct _BRB_L2CA_ACL_TRANSFER *brb;
    L2CAP_CHANNEL_HANDLE handle;
    NTSTATUS status;

    WdfWaitLockAcquire(DeviceContext->ConnectionLock, NULL);
    handle = DeviceContext->Connected ? DeviceContext->ChannelHandle : NULL;
    WdfWaitLockRelease(DeviceContext->ConnectionLock);

    if (handle == NULL) {
        return STATUS_INVALID_DEVICE_STATE;
    }

    brb = (struct _BRB_L2CA_ACL_TRANSFER *)
        DeviceContext->ProfileInterface.BthAllocateBrb(BRB_L2CA_ACL_TRANSFER, PODBRIDGE_POOL_TAG);
    if (brb == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    brb->BtAddress = DeviceContext->RemoteAddress;
    brb->ChannelHandle = handle;
    brb->TransferFlags = ACL_TRANSFER_DIRECTION_OUT;
    brb->BufferSize = FrameLength;
    brb->Buffer = Frame;
    brb->BufferMDL = NULL;

    status = PodBridgeSubmitBrbSync(DeviceContext->IoTarget, (PBRB)brb, sizeof(*brb));
    DeviceContext->ProfileInterface.BthFreeBrb((PBRB)brb);
    return status;
}
