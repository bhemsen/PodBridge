namespace PodBridge.Core.Bluetooth;

/// <summary>
/// OS-boundary source of Bluetooth-radio power-state transitions (on/off), used by
/// <see cref="BleScannerSupervisor"/> to restart the driver-free BLE watcher cleanly
/// after the user toggles the radio. Tier 1: no driver, no admin. Implemented on
/// Windows via <c>Windows.Devices.Radios.Radio</c>; Core stays OS-free and depends
/// only on this abstraction (constitution: all OS access behind interfaces).
/// </summary>
/// <remarks>
/// Only the <b>transition</b> matters here — the WinRT advertisement watcher does not
/// resurrect scanning on its own after the radio was powered off (its status goes to
/// Aborted), so the supervisor forces a fresh watcher on the off→on edge.
/// </remarks>
public interface IBluetoothRadioSource
{
    /// <summary>
    /// Raised when the Bluetooth radio's power state changes; the argument is
    /// <see langword="true"/> when the radio is on/available, <see langword="false"/>
    /// when it is off.
    /// </summary>
    event EventHandler<bool>? RadioStateChanged;

    /// <summary>Begins monitoring the radio power state. Idempotent.</summary>
    void Start();

    /// <summary>Stops monitoring and releases any OS resources held. Idempotent.</summary>
    void Stop();
}
