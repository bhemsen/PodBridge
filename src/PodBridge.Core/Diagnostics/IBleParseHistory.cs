namespace PodBridge.Core.Diagnostics;

/// <summary>
/// A bounded, most-recent-first history of BLE Continuity parse attempts for the
/// diagnostics snapshot (spec docs/specs/spec-model-coverage-hardening.md). Implemented
/// by <see cref="BleParseHistoryRecorder"/>; a fake substitutes it in
/// device-independent tests.
/// </summary>
public interface IBleParseHistory
{
    /// <summary>
    /// The most recent parse results, oldest first, capped at a small fixed count
    /// (<see cref="BleParseHistoryRecorder.Capacity"/>) so the diagnostics snapshot never
    /// grows unbounded.
    /// </summary>
    IReadOnlyList<BleParseResult> Recent { get; }
}
