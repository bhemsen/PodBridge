namespace PodBridge.Core.Models;

/// <summary>
/// AirPods noise-control mode. Values match the AAP control command
/// (opcode 0x09 / sub 0x0D) documented in docs/prior-art.md.
/// </summary>
public enum NoiseControlMode
{
    Off = 1,
    NoiseCancellation = 2,
    Transparency = 3,
    Adaptive = 4,
}
