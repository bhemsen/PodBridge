namespace PodBridge.Core.Capabilities;

/// <summary>
/// The (model, firmware-major) capability verdict from <see cref="CapabilityMatrix"/>,
/// independent of driver presence. <see cref="ICapabilityProvider"/> combines it with the
/// driver-presence check to produce the final <see cref="CapabilityDecision"/>.
/// </summary>
public enum Tier2Support
{
    /// <summary>The model supports the feature and no firmware-major refinement marks it off.</summary>
    Supported,

    /// <summary>
    /// The model (chip / sensor generation) does not have the feature at all — a hardware
    /// fact independent of firmware (e.g. AirPods 2 has no ANC hardware).
    /// </summary>
    ModelUnsupported,

    /// <summary>
    /// The model would support the feature but the specific firmware-major read from the
    /// device is explicitly marked unsupported. Reserved for a future, QA-confirmed
    /// firmware-varying refinement — the matrix ships with none today
    /// (docs/research/firmware-capabilities.md: the firmware-major dimension is a no-op).
    /// </summary>
    FirmwareUnsupported,
}
