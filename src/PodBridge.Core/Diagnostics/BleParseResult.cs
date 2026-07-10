using PodBridge.Core.Models;

namespace PodBridge.Core.Diagnostics;

/// <summary>
/// One entry in the diagnostics snapshot's recent-BLE-parse history: whether an
/// Apple-company-id advertisement decoded as a valid Continuity proximity frame, the
/// model it identified (if any), and the advertiser's Bluetooth address with only the
/// last two octets kept (<see cref="MaskedAddress"/>) — the full 48-bit address is
/// <b>never</b> retained here (constitution: local-only, no durable identifier leak).
/// </summary>
public sealed record BleParseResult
{
    /// <summary>
    /// <see langword="true"/> when <see cref="Protocol.ContinuityParser.TryParse"/>
    /// decoded a valid proximity-pairing frame from the advertisement.
    /// </summary>
    public required bool ParsedSuccessfully { get; init; }

    /// <summary>
    /// The advertiser's Bluetooth address with the first four octets replaced by
    /// <c>**</c>, e.g. <c>**:**:**:**:AB:CD</c> — short-lived correlation only, never a
    /// durable identifier (mirrors <see cref="Bluetooth.BleAdvertisement.Address"/>'s own
    /// "never a stable identity" note).
    /// </summary>
    public required string MaskedAddress { get; init; }

    /// <summary>
    /// The identified model when <see cref="ParsedSuccessfully"/>, or
    /// <see langword="null"/> when the frame did not decode.
    /// </summary>
    public AirPodsModel? Model { get; init; }
}
