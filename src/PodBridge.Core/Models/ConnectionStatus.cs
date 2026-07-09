namespace PodBridge.Core.Models;

/// <summary>
/// Aggregate connection state of a paired AirPods device, as surfaced by
/// <see cref="Bluetooth.IConnectionMonitor"/>. Drives the tray status line and
/// first-run pairing guidance. Ordered least-to-most connected so callers may
/// compare, but the numeric values are not a protocol contract.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>Initial state before the monitor has determined anything.</summary>
    Unknown = 0,

    /// <summary>No usable Bluetooth radio is present; the app still runs (never crashes).</summary>
    BluetoothUnavailable = 1,

    /// <summary>Bluetooth is available but no paired AirPods device was found (drives pairing guidance).</summary>
    NoDevice = 2,

    /// <summary>A paired AirPods device exists but is not currently connected.</summary>
    Disconnected = 3,

    /// <summary>A paired AirPods device is connected.</summary>
    Connected = 4,
}
