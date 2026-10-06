namespace PodBridge.Core.Bluetooth;

/// <summary>
/// The Bluetooth audio link state of the paired AirPods as reported by an
/// <see cref="IBluetoothAudioConnector"/>.
/// </summary>
public enum BluetoothAudioLinkState
{
    /// <summary>No paired AirPods audio device is known to Windows.</summary>
    NotFound = 0,

    /// <summary>The AirPods are paired but their audio link is down.</summary>
    Disconnected = 1,

    /// <summary>The AirPods audio link is up (an AirPods audio endpoint is active).</summary>
    Connected = 2,
}
