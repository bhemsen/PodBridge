namespace PodBridge.Core.Bluetooth;

/// <summary>
/// What the OS did with a single <see cref="BluetoothAudioRequest"/>. <see cref="Accepted"/>
/// only means the Bluetooth audio driver took the request — not that the link changed;
/// callers confirm the outcome through <see cref="IBluetoothAudioConnector.GetLinkState"/>.
/// </summary>
public enum BluetoothAudioRequestResult
{
    /// <summary>At least one Bluetooth audio driver accepted the request.</summary>
    Accepted = 0,

    /// <summary>No paired AirPods audio device was found to send the request to.</summary>
    NotFound = 1,

    /// <summary>The AirPods expose no Bluetooth audio connection control.</summary>
    Unsupported = 2,

    /// <summary>Every Bluetooth audio driver rejected the request (or the OS call failed).</summary>
    Rejected = 3,
}
