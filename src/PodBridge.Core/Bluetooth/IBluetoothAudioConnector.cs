namespace PodBridge.Core.Bluetooth;

/// <summary>
/// OS boundary for bringing the already-paired AirPods' Bluetooth audio link up or down
/// driver-free and admin-free (Tier 1). The Windows adapter sends the same one-shot
/// request the Windows Sound control panel "Connect" / "Disconnect" uses to the AirPods'
/// Bluetooth audio drivers (A2DP and hands-free). Both members are synchronous,
/// short OS calls that never throw: failures degrade to <see cref="BluetoothAudioLinkState.NotFound"/>
/// or a non-<see cref="BluetoothAudioRequestResult.Accepted"/> result. The retry/timeout
/// policy lives in Core (<see cref="BluetoothAudioLinkController"/>), not here.
/// </summary>
public interface IBluetoothAudioConnector
{
    /// <summary>Reads the current audio link state of the paired AirPods.</summary>
    BluetoothAudioLinkState GetLinkState();

    /// <summary>
    /// Sends one <paramref name="request"/> to the AirPods' Bluetooth audio drivers.
    /// <see cref="BluetoothAudioRequestResult.Accepted"/> does not mean the link changed.
    /// </summary>
    BluetoothAudioRequestResult SendRequest(BluetoothAudioRequest request);
}
