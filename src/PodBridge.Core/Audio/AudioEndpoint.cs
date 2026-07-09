namespace PodBridge.Core.Audio;

/// <summary>
/// A platform-neutral audio endpoint the mic-profile policy routes roles across.
/// Enumerated by <see cref="IAudioPolicy"/>; no OS type leaks into Core.
/// </summary>
/// <param name="Id">
/// Opaque, stable endpoint identifier (the Windows adapter's endpoint device id).
/// The engine only compares and echoes it back to <see cref="IAudioPolicy"/>; it
/// never parses it.
/// </param>
/// <param name="Direction">Whether this is a render (output) or capture (input) endpoint.</param>
/// <param name="IsAirPods">
/// <c>true</c> when the adapter has identified this endpoint as the connected
/// AirPods (by MMDevice container-id, friendly-name fallback). <b>Core routes every
/// role purely on this flag and never identifies the endpoint itself</b> — endpoint
/// identification is the Windows adapter's job (spec prior decision), and it is a
/// distinct mapping from the Phase 1–2 Bluetooth-device identification.
/// </param>
/// <param name="FriendlyName">Optional human-readable name for display / diagnostics.</param>
public sealed record AudioEndpoint(
    string Id,
    AudioEndpointDirection Direction,
    bool IsAirPods,
    string? FriendlyName = null);
