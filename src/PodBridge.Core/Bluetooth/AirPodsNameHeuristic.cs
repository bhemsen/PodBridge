namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Phase-1 device identification: a paired Bluetooth device is treated as an
/// AirPods/Beats accessory when its friendly name contains "AirPods" or "Beats"
/// (case-insensitive). Company-id based matching from the BLE-advertisement path
/// replaces this in Phase 2 (see docs/specs/spec-foundation-pairing.md).
/// </summary>
public static class AirPodsNameHeuristic
{
    private static readonly string[] Needles = ["AirPods", "Beats"];

    /// <summary>True when <paramref name="deviceName"/> names an AirPods/Beats device.</summary>
    public static bool IsMatch(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        foreach (var needle in Needles)
        {
            if (deviceName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
