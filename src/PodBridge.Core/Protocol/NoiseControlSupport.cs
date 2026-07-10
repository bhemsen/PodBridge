using PodBridge.Core.Models;

namespace PodBridge.Core.Protocol;

/// <summary>
/// Which noise-control modes a connected AirPods model exposes. Device-independent
/// business logic (kept in OS-free Core so the tray can gate its menu without a
/// hardware dependency). Phase 6 targets the AirPods Pro 2 reference model; the
/// broad model/firmware matrix is Phase 8 (docs/research/aap-anc-protocol.md
/// "Model / firmware support").
/// </summary>
public static class NoiseControlSupport
{
    /// <summary>
    /// True when <paramref name="model"/> exposes the Adaptive mode. Per Apple's
    /// mode-support matrix (research source 4, authoritative) Adaptive (wire byte
    /// <c>0x04</c>) is offered on AirPods Pro 2 only among the Phase-6 target models —
    /// not 1st-gen Pro, not Max — so the tray gates the Adaptive entry on this and
    /// offers it only where reported/supported. (docs/research/aap-anc-protocol.md
    /// decision "Gate Adaptive on model = AirPods Pro 2 (reference)".)
    /// </summary>
    public static bool SupportsAdaptive(AirPodsModel model) =>
        model is AirPodsModel.AirPodsPro2 or AirPodsModel.AirPodsPro2UsbC;
}
