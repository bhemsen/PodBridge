/*++

Module: Driver.c

    DriverEntry for the PodBridge AAP KMDF L2CAP bridge. Registers the single
    device-add callback; everything else hangs off the per-device context.

    Clean-room from research #40 (standard KMDF driver bring-up).

--*/

#include "Driver.h"

_Use_decl_annotations_
NTSTATUS
DriverEntry(
    PDRIVER_OBJECT DriverObject,
    PUNICODE_STRING RegistryPath)
{
    WDF_DRIVER_CONFIG config;

    WDF_DRIVER_CONFIG_INIT(&config, PodBridgeEvtDeviceAdd);

    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}
