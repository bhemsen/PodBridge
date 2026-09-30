namespace PodBridge.Core.Bluetooth;

/// <summary>A one-shot request to bring the paired AirPods' audio link up or down.</summary>
public enum BluetoothAudioRequest
{
    /// <summary>Connect the already-paired AirPods.</summary>
    Connect = 0,

    /// <summary>Disconnect the AirPods (they stay paired).</summary>
    Disconnect = 1,
}
