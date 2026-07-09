namespace PodBridge.Core.Audio;

/// <summary>
/// The data-flow direction of an audio endpoint, mirroring the Windows
/// <c>EDataFlow</c> enum. An endpoint is either an output (<see cref="Render"/>)
/// or an input (<see cref="Capture"/>); its role defaults are set independently.
/// </summary>
public enum AudioEndpointDirection
{
    /// <summary>An output endpoint (speakers / headphones) — <c>eRender</c>.</summary>
    Render,

    /// <summary>An input endpoint (microphone) — <c>eCapture</c>.</summary>
    Capture,
}
