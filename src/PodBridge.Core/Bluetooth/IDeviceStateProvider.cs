using PodBridge.Core.Models;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Read-only source of the current, connection-gated AirPods <see cref="DeviceState"/>.
/// The clean abstraction downstream consumers (tray battery display, auto play/pause
/// engine) depend on, so they never touch the scanner, parser, or connection monitor
/// directly. Implemented by <see cref="DeviceStateTracker"/>.
/// </summary>
public interface IDeviceStateProvider
{
    /// <summary>The latest state — <see cref="DeviceState.Unknown"/> until live telemetry arrives.</summary>
    DeviceState Current { get; }

    /// <summary>Raised when <see cref="Current"/> changes; the argument is the new state.</summary>
    event EventHandler<DeviceState>? StateChanged;
}
