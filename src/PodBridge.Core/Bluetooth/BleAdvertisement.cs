namespace PodBridge.Core.Bluetooth;

/// <summary>
/// A single raw BLE advertisement as delivered by the driver-free Tier-1 scanner
/// (WinRT advertisement watcher on Windows). Carries only wire data — no decoding
/// happens in the adapter; the Continuity payload is decoded in Core by
/// <see cref="Protocol.ContinuityParser"/>. This keeps decode logic OS-free and
/// unit-testable (constitution: Core is platform-neutral).
/// </summary>
/// <param name="Address">
/// The advertiser's Bluetooth device address (48-bit, right-aligned in the
/// <see cref="ulong"/>). Apple rotates random addresses, so this is used only for
/// short-lived same-device correlation, never as a stable identity.
/// </param>
/// <param name="RssiDbm">Received signal strength in dBm (nearer ≈ larger/less-negative).</param>
/// <param name="CompanyId">
/// Manufacturer-specific-data company id (Bluetooth SIG). Apple = <c>0x004C</c>.
/// </param>
/// <param name="ManufacturerData">
/// The manufacturer-specific-data payload with the company id already stripped, so
/// index 0 is the first Continuity TLV type byte (matches WinRT
/// <c>BluetoothLEManufacturerData.Data</c>).
/// </param>
public sealed record BleAdvertisement(
    ulong Address,
    short RssiDbm,
    ushort CompanyId,
    IReadOnlyList<byte> ManufacturerData);
