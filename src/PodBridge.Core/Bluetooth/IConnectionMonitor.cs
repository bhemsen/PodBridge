using PodBridge.Core.Models;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Detects whether a paired AirPods device is present and connected on the host,
/// and raises live connect/disconnect transitions. OS-free abstraction (Tier 1:
/// no driver, no admin); implemented on Windows via WinRT in
/// <c>WinRtConnectionMonitor</c>. Consumers must handle
/// <see cref="ConnectionStatus.BluetoothUnavailable"/> gracefully (no radio).
/// </summary>
public interface IConnectionMonitor
{
    /// <summary>The most recently observed aggregate connection status.</summary>
    ConnectionStatus CurrentStatus { get; }

    /// <summary>Raised when <see cref="CurrentStatus"/> changes; the argument is the new status.</summary>
    event EventHandler<ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Begins monitoring and determines the initial status. Idempotent: a second
    /// call while already running is a no-op.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops monitoring and releases any OS resources held.</summary>
    void Stop();
}
