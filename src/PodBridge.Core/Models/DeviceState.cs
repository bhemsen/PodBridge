namespace PodBridge.Core.Models;

/// <summary>
/// Immutable snapshot of an AirPods device's observable state. Battery, charging
/// and in-ear fields come from the passive BLE advertisement path (Tier 1);
/// noise-control requires the optional Tier-2 transport.
/// <para>
/// <see cref="IsLive"/> distinguishes a fresh, connection-gated telemetry snapshot
/// from the "unknown / out of range" state (see <see cref="Unknown"/>) shown when
/// no AirPods are connected or the advertisement has gone stale. A percentage of
/// <see langword="null"/> means "unknown" for that component even when live (the
/// proximity message encodes an unknown-battery sentinel).
/// </para>
/// </summary>
public sealed record DeviceState
{
    /// <summary>Battery % of the left bud, or <see langword="null"/> if unknown/absent.</summary>
    public int? LeftBatteryPercent { get; init; }

    /// <summary>Battery % of the right bud, or <see langword="null"/> if unknown/absent.</summary>
    public int? RightBatteryPercent { get; init; }

    /// <summary>Battery % of the case, or <see langword="null"/> if unknown/absent.</summary>
    public int? CaseBatteryPercent { get; init; }

    /// <summary>True while the left bud is charging.</summary>
    public bool LeftCharging { get; init; }

    /// <summary>True while the right bud is charging.</summary>
    public bool RightCharging { get; init; }

    /// <summary>True while the case is charging.</summary>
    public bool CaseCharging { get; init; }

    /// <summary>True while the left bud is in an ear.</summary>
    public bool LeftInEar { get; init; }

    /// <summary>True while the right bud is in an ear.</summary>
    public bool RightInEar { get; init; }

    /// <summary>The identified AirPods/Beats model, or <see cref="AirPodsModel.Unknown"/>.</summary>
    public AirPodsModel Model { get; init; } = AirPodsModel.Unknown;

    /// <summary>
    /// True when this snapshot carries fresh, connection-gated telemetry. When
    /// false, the device is disconnected or out of range and the battery/in-ear
    /// fields carry no live meaning — the tray shows "unknown / out of range".
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>True when at least one bud is in an ear (drives auto play/pause).</summary>
    public bool AnyInEar => LeftInEar || RightInEar;

    /// <summary>
    /// The canonical "unknown / out of range" state: not live, no battery, no
    /// in-ear. Used when no AirPods are connected or telemetry has gone stale.
    /// </summary>
    public static DeviceState Unknown { get; } = new();
}
