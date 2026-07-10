using PodBridge.Core.Models;

namespace PodBridge.Core.Capabilities;

/// <summary>
/// One entry marking a specific (feature, model, firmware-major) tuple as <b>unsupported</b>
/// — the sole way the firmware-major dimension can turn a Tier-2 feature off on a model that
/// otherwise supports it.
/// <para>
/// The shipped set is <b>empty</b>: research #51 (docs/research/firmware-capabilities.md) found
/// no source documenting a firmware-major that toggles a whole Tier-2 feature on
/// otherwise-identical hardware, so the firmware-major dimension is a documented no-op until
/// real-hardware QA proves a genuine firmware-varying refinement. The type exists so a future
/// refinement is a data edit (add an entry) rather than a code change, and so the gating logic
/// for it is exercised by tests.
/// </para>
/// </summary>
public readonly record struct FirmwareRefinement(Tier2Feature Feature, AirPodsModel Model, int FirmwareMajor);
