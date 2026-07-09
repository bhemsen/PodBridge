namespace PodBridge.Core.Models;

/// <summary>
/// AirPods / Beats model identified from the Continuity proximity-pairing model-id
/// bytes (offsets 3–4). Enum values are the <b>little-endian</b> model constant
/// (<c>model = offset3 | offset4 &lt;&lt; 8</c> → the <c>0x20xx</c> family), the
/// convention recommended by docs/research/continuity-parser.md (matches
/// AirPodsDesktop / the AAP protocol constants). An unrecognised id maps to
/// <see cref="Unknown"/>; identification on the advertisement path is by company
/// id <c>0x004C</c> first, with the model only labelling a confirmed AirPods frame.
/// </summary>
public enum AirPodsModel : ushort
{
    /// <summary>Model id not in the known table (still a valid AirPods/Beats frame).</summary>
    Unknown = 0x0000,

    // Model constants: docs/research/continuity-parser.md "Model id (offsets 3–4)",
    // little-endian 0x20xx table (research sources 2 + 4, byte-swap-consistent).
    AirPods1 = 0x2002,
    AirPods2 = 0x200F,
    AirPods3 = 0x2013,
    AirPods4 = 0x2019,
    AirPods4Anc = 0x201B,
    AirPodsPro = 0x200E,
    AirPodsPro2 = 0x2014,
    AirPodsPro2UsbC = 0x2024,
    AirPodsPro3 = 0x2027,
    AirPodsMax = 0x200A,
    AirPodsMaxUsbC = 0x201F,
    BeatsFitPro = 0x2012,
}
