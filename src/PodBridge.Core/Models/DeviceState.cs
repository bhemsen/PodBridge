namespace PodBridge.Core.Models;

/// <summary>
/// Immutable snapshot of an AirPods device's observable state. Battery and
/// in-ear fields come from the passive BLE advertisement path (Tier 1);
/// noise-control requires the optional Tier-2 transport.
/// </summary>
public sealed record DeviceState
{
    public int? LeftBatteryPercent { get; init; }

    public int? RightBatteryPercent { get; init; }

    public int? CaseBatteryPercent { get; init; }

    public bool LeftInEar { get; init; }

    public bool RightInEar { get; init; }

    /// <summary>True when at least one bud is in an ear (drives auto play/pause).</summary>
    public bool AnyInEar => LeftInEar || RightInEar;
}
