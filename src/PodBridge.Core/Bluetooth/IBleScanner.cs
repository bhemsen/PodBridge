using PodBridge.Core.Models;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Passive BLE advertisement source for AirPods state. Tier 1: no driver, no
/// admin. Implemented on Windows via the WinRT advertisement watcher.
/// </summary>
public interface IBleScanner
{
    /// <summary>Raised when a decoded AirPods state snapshot is available.</summary>
    event EventHandler<DeviceState>? StateChanged;

    void Start();

    void Stop();
}
