namespace PodBridge.Core.Models;

/// <summary>
/// The action assignable to the AirPods <b>press-and-hold</b> stem gesture. Enum values
/// equal the AAP control-command action bytes documented for identifier <c>0x16</c>
/// (ClickHoldMode): Noise Control = <c>0x01</c>, Siri / voice assistant = <c>0x05</c>
/// (docs/research/gesture-aap.md "Gesture-remap command → Action enum"). These are the
/// <b>only</b> settable actions the research attests; single / double / triple presses
/// are fixed by Apple and are deliberately not represented here, so an unsupported
/// action can never be built or stored (spec: "no invented actions").
/// </summary>
public enum GestureAction
{
    /// <summary>Cycle the noise-control modes — wire byte <c>0x01</c>. (docs/research/gesture-aap.md.)</summary>
    NoiseControl = 0x01,

    /// <summary>Invoke Siri / the voice assistant — wire byte <c>0x05</c>. (docs/research/gesture-aap.md.)</summary>
    Siri = 0x05,
}
