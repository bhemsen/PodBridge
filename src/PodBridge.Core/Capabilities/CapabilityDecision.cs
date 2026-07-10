namespace PodBridge.Core.Capabilities;

/// <summary>
/// The final honest verdict for a Tier-2 feature: whether it is <see cref="IsAvailable"/>
/// and a human-readable <see cref="Reason"/> that is surfaced in the UI so a feature is
/// never silently missing and never falsely claimed (constitution: Tier-2 is opt-in and
/// honest). The reason is populated in every state, including the on-state.
/// </summary>
public sealed record CapabilityDecision
{
    /// <summary>Reason shown when the optional Tier-2 driver is absent (the Tier-1 default).</summary>
    public const string DriverAbsentReason = "requires the optional driver";

    /// <summary>Reason shown when the model (chip/sensor generation) lacks the feature entirely.</summary>
    public const string ModelUnsupportedReason = "not supported on this model";

    /// <summary>
    /// Reason shown when a readable firmware-major is explicitly marked unsupported. Reserved
    /// for a future firmware-varying refinement; the shipped matrix produces no such entry
    /// (docs/research/firmware-capabilities.md).
    /// </summary>
    public const string FirmwareUnsupportedReason = "not supported on this firmware";

    /// <summary>Reason shown in the on-state (driver present and the model/firmware support it).</summary>
    public const string SupportedReason = "supported";

    /// <summary>True only when the feature is offered; false disables it with an honest <see cref="Reason"/>.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>The honest, user-facing explanation for the current <see cref="IsAvailable"/> state.</summary>
    public required string Reason { get; init; }

    /// <summary>The on-state: driver present and the (model, firmware-major) capability holds.</summary>
    public static CapabilityDecision Available { get; } =
        new() { IsAvailable = true, Reason = SupportedReason };

    /// <summary>Off: the optional driver is absent.</summary>
    public static CapabilityDecision DriverAbsent { get; } =
        new() { IsAvailable = false, Reason = DriverAbsentReason };

    /// <summary>Off: the connected model does not have the feature.</summary>
    public static CapabilityDecision ModelUnsupported { get; } =
        new() { IsAvailable = false, Reason = ModelUnsupportedReason };

    /// <summary>Off: the device's firmware-major is explicitly marked unsupported.</summary>
    public static CapabilityDecision FirmwareUnsupported { get; } =
        new() { IsAvailable = false, Reason = FirmwareUnsupportedReason };
}
