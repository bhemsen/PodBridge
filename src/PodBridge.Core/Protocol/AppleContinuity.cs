namespace PodBridge.Core.Protocol;

/// <summary>
/// Helpers for Apple's BLE "Continuity" manufacturer data. AirPods broadcast
/// battery and in-ear state under Bluetooth SIG company id 0x004C; reading it
/// needs no connection, driver, or admin rights (see docs/prior-art.md, axis E2).
/// </summary>
public static class AppleContinuity
{
    /// <summary>Apple's Bluetooth SIG company identifier.</summary>
    public const ushort AppleCompanyId = 0x004C;

    /// <summary>
    /// True when a BLE advertisement's manufacturer-data company id is Apple's —
    /// the first filter on the driver-free advertisement path.
    /// </summary>
    public static bool IsAppleManufacturerData(ushort companyId) => companyId == AppleCompanyId;
}
