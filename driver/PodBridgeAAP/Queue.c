/*++

Module: Queue.c

    User-mode IOCTL dispatch for the PodBridge AAP bridge and the inverted-call
    RECEIVE plumbing.

      CONNECT -> open the L2CAP channel (idempotent).
      SEND    -> write one AAP frame over the channel.
      RECEIVE -> parked on the manual queue; completed by the receive thread with
                 the next inbound AAP frame (research #40 (b): inverted call).

    Clean-room from research #40.

--*/

#include "Driver.h"

static VOID
PodBridgeHandleSend(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request);

static VOID
PodBridgeHandleReceive(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request);

_Use_decl_annotations_
VOID
PodBridgeEvtIoDeviceControl(
    WDFQUEUE Queue,
    WDFREQUEST Request,
    size_t OutputBufferLength,
    size_t InputBufferLength,
    ULONG IoControlCode)
{
    PPODBRIDGE_DEVICE_CONTEXT context = PodBridgeGetDeviceContext(WdfIoQueueGetDevice(Queue));

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    switch (IoControlCode) {
    case IOCTL_PODBRIDGE_CONNECT:
        WdfRequestComplete(Request, PodBridgeConnectChannel(context));
        break;

    case IOCTL_PODBRIDGE_SEND:
        PodBridgeHandleSend(context, Request);
        break;

    case IOCTL_PODBRIDGE_RECEIVE:
        PodBridgeHandleReceive(context, Request);
        break;

    default:
        WdfRequestComplete(Request, STATUS_INVALID_DEVICE_REQUEST);
        break;
    }
}

static VOID
PodBridgeHandleSend(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request)
{
    PVOID buffer;
    size_t length;
    NTSTATUS status;

    status = WdfRequestRetrieveInputBuffer(Request, 1, &buffer, &length);
    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(Request, status);
        return;
    }
    if (length > PODBRIDGE_MAX_FRAME) {
        WdfRequestComplete(Request, STATUS_INVALID_BUFFER_SIZE);
        return;
    }

    status = PodBridgeSendFrame(Context, buffer, (ULONG)length);
    WdfRequestCompleteWithInformation(Request, status, NT_SUCCESS(status) ? length : 0);
}

//
// Park the RECEIVE request on the manual queue. It stays pending (inverted call)
// until the receive thread hands it an inbound frame, or the queue is drained on
// disconnect. Reject if not connected so callers do not wait on a dead channel.
//
static VOID
PodBridgeHandleReceive(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context,
    _In_ WDFREQUEST Request)
{
    NTSTATUS status;

    WdfWaitLockAcquire(Context->ConnectionLock, NULL);
    status = Context->Connected ? STATUS_SUCCESS : STATUS_INVALID_DEVICE_STATE;
    WdfWaitLockRelease(Context->ConnectionLock);

    if (NT_SUCCESS(status)) {
        status = WdfRequestForwardToIoQueue(Request, Context->ReceiveQueue);
        if (NT_SUCCESS(status)) {
            return;
        }
    }
    WdfRequestComplete(Request, status);
}

_Use_decl_annotations_
VOID
PodBridgeDeliverInboundFrame(
    PPODBRIDGE_DEVICE_CONTEXT DeviceContext,
    PVOID Frame,
    ULONG FrameLength)
{
    WDFREQUEST request;
    PVOID buffer;
    size_t bufferLength;
    NTSTATUS status;
    ULONG toCopy;

    //
    // Take the next parked RECEIVE request. If none is waiting, the frame is
    // dropped: the transport (#43) keeps a RECEIVE outstanding, so in practice
    // one is always available for the sparse AAP notifications.
    //
    status = WdfIoQueueRetrieveNextRequest(DeviceContext->ReceiveQueue, &request);
    if (!NT_SUCCESS(status)) {
        return;
    }

    status = WdfRequestRetrieveOutputBuffer(request, 1, &buffer, &bufferLength);
    if (!NT_SUCCESS(status)) {
        WdfRequestComplete(request, status);
        return;
    }

    toCopy = (FrameLength < bufferLength) ? FrameLength : (ULONG)bufferLength;
    RtlCopyMemory(buffer, Frame, toCopy);
    WdfRequestCompleteWithInformation(request, STATUS_SUCCESS, toCopy);
}
