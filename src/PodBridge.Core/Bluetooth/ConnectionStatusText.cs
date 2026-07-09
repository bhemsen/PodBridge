using PodBridge.Core.Models;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Pure, device-independent mapping from <see cref="ConnectionStatus"/> to the
/// short phrase shown on the tray status line, tooltip, and first-run guidance.
/// Kept in Core (no UI dependency) so it satisfies the constitution's Tier-1
/// device-independent test gate — see <c>ConnectionStatusTextTests</c>.
/// </summary>
public static class ConnectionStatusText
{
    /// <summary>Human-readable phrase for <paramref name="status"/> (never null/empty).</summary>
    public static string ForStatus(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "Connected",
        ConnectionStatus.Disconnected => "Disconnected",
        ConnectionStatus.NoDevice => "No AirPods paired",
        ConnectionStatus.BluetoothUnavailable => "Bluetooth unavailable",
        _ => "—",
    };
}
