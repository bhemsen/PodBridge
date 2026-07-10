using PodBridge.Core.Audio;
using PodBridge.Core.Models;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// A local, human-readable diagnostics snapshot for bug reports (spec
/// docs/specs/spec-model-coverage-hardening.md). Built by
/// <see cref="DiagnosticsSnapshotFactory"/> and exported by
/// <see cref="IDiagnosticsExporter"/>.
/// <para>
/// <b>Deterministic:</b> given the same inputs this record always compares equal (no
/// timestamp, no random value, no machine-specific path in its content) — a snapshot
/// unit test can assert equality directly. <b>Local-only:</b> it carries no secret, token,
/// or key, and the Bluetooth address is never present in full — only the masked form
/// inside <see cref="RecentBleParses"/> (constitution: local-only).
/// </para>
/// </summary>
public sealed record DiagnosticsSnapshot
{
    /// <summary>The identified model, or <see cref="AirPodsModel.Unknown"/>.</summary>
    public required AirPodsModel Model { get; init; }

    /// <summary>The model's human-readable name, e.g. "AirPods Pro" or "Unknown AirPods".</summary>
    public required string ModelDisplayName { get; init; }

    /// <summary>
    /// The firmware-major version, or <see langword="null"/> when unreadable — which is
    /// every reading today, since no host-requestable firmware-version read exists on the
    /// cleartext AAP channel (docs/research/firmware-capabilities.md).
    /// </summary>
    public int? FirmwareMajor { get; init; }

    /// <summary>The actually-negotiated A2DP media codec (Phase 3), reported honestly.</summary>
    public required CodecKind Codec { get; init; }

    /// <summary>Human-readable tier label, e.g. "Tier 1 (driver-free)".</summary>
    public required string Tier { get; init; }

    /// <summary>Whether the optional Tier-2 driver is currently loaded.</summary>
    public required bool DriverPresent { get; init; }

    /// <summary>
    /// The honest driver signing/test-mode fact for the current
    /// <see cref="DriverPresent"/> state (<see cref="Diagnostics.DriverSigningStatus"/>).
    /// </summary>
    public required string DriverSigningStatus { get; init; }

    /// <summary>The full Tier-1 + Tier-2 capability matrix for the connected model.</summary>
    public required IReadOnlyList<CapabilityMatrixEntry> Capabilities { get; init; }

    /// <summary>
    /// The most recent BLE Continuity parse results, address-masked (see
    /// <see cref="BleParseResult.MaskedAddress"/>).
    /// </summary>
    public required IReadOnlyList<BleParseResult> RecentBleParses { get; init; }
}
