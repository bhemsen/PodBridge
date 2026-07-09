namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Passive BLE advertisement source. Tier 1: no driver, no admin. Emits <b>raw</b>
/// advertisement data only — decoding the Apple-Continuity payload is Core's job
/// (<see cref="Protocol.ContinuityParser"/> via <see cref="DeviceStateTracker"/>),
/// not the adapter's. Implemented on Windows via the WinRT advertisement watcher.
/// </summary>
public interface IBleScanner
{
    /// <summary>Raised for each raw BLE advertisement observed while scanning.</summary>
    event EventHandler<BleAdvertisement>? AdvertisementReceived;

    /// <summary>Begins scanning. Idempotent.</summary>
    void Start();

    /// <summary>Stops scanning and releases any OS resources held.</summary>
    void Stop();
}
