/*++

Module: Device.c

    Device bring-up for the PodBridge AAP KMDF L2CAP bridge: the FDO that BthEnum
    loads over its per-service PDO. Creates the per-device context, the default
    (device-control) queue, the manual RECEIVE queue, and the user-mode device
    interface; and, in PrepareHardware, acquires the Bluetooth profile-driver
    interface and the remote AirPods address.

    Defining INITGUID here (once) emits storage for GUID_DEVINTERFACE_PODBRIDGE_AAP
    and the Bluetooth DDI GUIDs used across the driver.

    Clean-room from research #40 ((a) target device via GET_DEVINFO, profile
    interface via QUERY_INTERFACE; (b) WdfDeviceCreateDeviceInterface).

--*/

#include <initguid.h>
#include "Driver.h"

static NTSTATUS
PodBridgeCreateQueues(_In_ WDFDEVICE Device, _In_ PPODBRIDGE_DEVICE_CONTEXT Context);

static NTSTATUS
PodBridgeQueryRemoteAddress(_In_ PPODBRIDGE_DEVICE_CONTEXT Context);

_Use_decl_annotations_
NTSTATUS
PodBridgeEvtDeviceAdd(
    WDFDRIVER Driver,
    PWDFDEVICE_INIT DeviceInit)
{
    WDF_PNPPOWER_EVENT_CALLBACKS pnp;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDFDEVICE device;
    PPODBRIDGE_DEVICE_CONTEXT context;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(Driver);

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnp);
    pnp.EvtDevicePrepareHardware = PodBridgeEvtDevicePrepareHardware;
    pnp.EvtDeviceReleaseHardware = PodBridgeEvtDeviceReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnp);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, PODBRIDGE_DEVICE_CONTEXT);

    status = WdfDeviceCreate(&DeviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context = PodBridgeGetDeviceContext(device);
    RtlZeroMemory(context, sizeof(*context));
    context->Device = device;
    context->IoTarget = WdfDeviceGetIoTarget(device);

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfWaitLockCreate(&attributes, &context->ConnectionLock);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = PodBridgeCreateQueues(device, context);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    return WdfDeviceCreateDeviceInterface(
        device,
        &GUID_DEVINTERFACE_PODBRIDGE_AAP,
        NULL);
}

//
// Default queue dispatches the user-mode IOCTLs sequentially (so connect/send
// are serialized without an extra lock); the manual queue parks RECEIVE IOCTLs.
//
static NTSTATUS
PodBridgeCreateQueues(
    _In_ WDFDEVICE Device,
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFQUEUE queue;
    NTSTATUS status;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = PodBridgeEvtIoDeviceControl;

    status = WdfIoQueueCreate(Device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &queue);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchManual);
    return WdfIoQueueCreate(Device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &Context->ReceiveQueue);
}

_Use_decl_annotations_
NTSTATUS
PodBridgeEvtDevicePrepareHardware(
    WDFDEVICE Device,
    WDFCMRESLIST ResourcesRaw,
    WDFCMRESLIST ResourcesTranslated)
{
    PPODBRIDGE_DEVICE_CONTEXT context = PodBridgeGetDeviceContext(Device);
    NTSTATUS status;

    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    //
    // Query the Bluetooth profile-driver interface: the BthAllocateBrb /
    // BthFreeBrb / BthInitializeBrb / BthReuseBrb helpers every BRB needs
    // (research #40 (a) -- QUERY_INTERFACE for GUID_BTHDDI_PROFILE_DRIVER_INTERFACE).
    //
    status = WdfFdoQueryForInterface(
        Device,
        &GUID_BTHDDI_PROFILE_DRIVER_INTERFACE,
        (PINTERFACE)&context->ProfileInterface,
        sizeof(context->ProfileInterface),
        BTHDDI_PROFILE_DRIVER_INTERFACE_VERSION_FOR_QI,
        NULL);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = PodBridgeQueryRemoteAddress(context);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    context->HardwareReady = TRUE;
    return STATUS_SUCCESS;
}

//
// Ask BthEnum which remote device this FDO was loaded for. The AirPods address
// it returns is what BRB_L2CA_OPEN_CHANNEL.BtAddress needs -- the driver never
// guesses the address (research #40 (a): IOCTL_INTERNAL_BTHENUM_GET_DEVINFO).
//
static NTSTATUS
PodBridgeQueryRemoteAddress(
    _In_ PPODBRIDGE_DEVICE_CONTEXT Context)
{
    BTH_DEVICE_INFO deviceInfo;
    WDF_MEMORY_DESCRIPTOR outputDesc;
    NTSTATUS status;

    RtlZeroMemory(&deviceInfo, sizeof(deviceInfo));
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(&outputDesc, &deviceInfo, sizeof(deviceInfo));

    status = WdfIoTargetSendInternalIoctlSynchronously(
        Context->IoTarget,
        NULL,
        IOCTL_INTERNAL_BTHENUM_GET_DEVINFO,
        NULL,
        &outputDesc,
        NULL,
        NULL);
    if (NT_SUCCESS(status)) {
        Context->RemoteAddress = deviceInfo.address;
    }
    return status;
}

_Use_decl_annotations_
NTSTATUS
PodBridgeEvtDeviceReleaseHardware(
    WDFDEVICE Device,
    WDFCMRESLIST ResourcesTranslated)
{
    PPODBRIDGE_DEVICE_CONTEXT context = PodBridgeGetDeviceContext(Device);

    UNREFERENCED_PARAMETER(ResourcesTranslated);

    //
    // Tear the L2CAP channel down on removal; safe (a no-op) if never connected.
    //
    PodBridgeDisconnectChannel(context);
    context->HardwareReady = FALSE;
    return STATUS_SUCCESS;
}
