namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Phase-1 device identification: a paired Bluetooth device is treated as an
/// AirPods/Beats accessory when its friendly name contains "AirPods" or "Beats"
/// (case-insensitive). Company-id based matching from the BLE-advertisement path
/// replaces this in Phase 2 (see docs/specs/archive/spec-foundation-pairing.md).
/// <see cref="IsAirPodsName"/> is the stricter AirPods-only variant for actions whose
/// label names AirPods specifically.
/// </summary>
public static class AirPodsNameHeuristic
{
    private const string AirPodsNeedle = "AirPods";

    private static readonly string[] Needles = [AirPodsNeedle, "Beats"];

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

    /// <summary>
    /// True when <paramref name="deviceName"/> names AirPods specifically (Beats excluded) —
    /// used by actions labelled "AirPods", such as one-click Connect / Disconnect, so they
    /// never act on a paired Beats device.
    /// </summary>
    public static bool IsAirPodsName(string? deviceName)
        => !string.IsNullOrWhiteSpace(deviceName)
            && deviceName.Contains(AirPodsNeedle, StringComparison.OrdinalIgnoreCase);
}
