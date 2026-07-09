using System.Globalization;

namespace PodBridge.Core.Models;

/// <summary>
/// Pure, device-independent mapping from a <see cref="DeviceState"/> to the short
/// battery phrase shown on the tray (context-menu line + tooltip). Renders the left
/// bud, right bud, and case as 10%-granularity percentages with a charging
/// indicator, an explicit per-component "unknown" for the absent-battery sentinel,
/// and a single <see cref="OutOfRange"/> phrase when the snapshot is not live
/// (disconnected or past the staleness timeout). Kept in Core (no UI dependency) so
/// it satisfies the constitution's Tier-1 device-independent test gate — see
/// <c>BatteryStatusTextTests</c>.
/// </summary>
public static class BatteryStatusText
{
    /// <summary>Shown when telemetry is not live (disconnected / stale / no AirPods).</summary>
    public const string OutOfRange = "unknown / out of range";

    /// <summary>Per-component text when the battery nibble is the unknown sentinel.</summary>
    private const string UnknownComponent = "unknown";

    // Charging indicator appended to a component; separator between components.
    private const string ChargingMark = "⚡";
    private const string Separator = " · ";

    /// <summary>
    /// Battery phrase for <paramref name="state"/> (never null/empty). Returns
    /// <see cref="OutOfRange"/> unless the snapshot is live; otherwise renders
    /// left / right / case with a charging indicator.
    /// </summary>
    public static string ForState(DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsLive)
        {
            return OutOfRange;
        }

        return string.Join(
            Separator,
            Component("L", state.LeftBatteryPercent, state.LeftCharging),
            Component("R", state.RightBatteryPercent, state.RightCharging),
            Component("Case", state.CaseBatteryPercent, state.CaseCharging));
    }

    private static string Component(string label, int? percent, bool charging)
    {
        var value = percent.HasValue
            ? percent.Value.ToString(CultureInfo.InvariantCulture) + "%"
            : UnknownComponent;
        var mark = charging ? ChargingMark : string.Empty;
        return $"{label} {value}{mark}";
    }
}
