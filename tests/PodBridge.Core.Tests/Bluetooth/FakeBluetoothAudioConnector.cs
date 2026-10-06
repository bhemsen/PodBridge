using PodBridge.Core.Bluetooth;

namespace PodBridge.Core.Tests.Bluetooth;

/// <summary>
/// Device-independent stand-in for the Windows Bluetooth audio connector. It records every
/// request and simulates the real driver's behaviour seen on hardware: an accepted request
/// may be silently ignored while a previous link winds down (<see cref="IgnoredRequests"/>),
/// or never answered at all (<see cref="Responds"/> false, e.g. AirPods still in the closed
/// case). Lets the Tier-1 test gate run with no physical AirPods.
/// </summary>
internal sealed class FakeBluetoothAudioConnector : IBluetoothAudioConnector
{
    public BluetoothAudioLinkState State { get; set; } = BluetoothAudioLinkState.Disconnected;

    /// <summary>What every <see cref="SendRequest"/> returns.</summary>
    public BluetoothAudioRequestResult RequestResult { get; set; } = BluetoothAudioRequestResult.Accepted;

    /// <summary>How many accepted requests are dropped before one changes the link.</summary>
    public int IgnoredRequests { get; set; }

    /// <summary>When false, accepted requests never change the link.</summary>
    public bool Responds { get; set; } = true;

    public List<BluetoothAudioRequest> Requests { get; } = [];

    public BluetoothAudioLinkState GetLinkState() => State;

    public BluetoothAudioRequestResult SendRequest(BluetoothAudioRequest request)
    {
        Requests.Add(request);
        if (RequestResult != BluetoothAudioRequestResult.Accepted)
        {
            return RequestResult;
        }

        if (Responds && Requests.Count > IgnoredRequests)
        {
            State = request == BluetoothAudioRequest.Connect
                ? BluetoothAudioLinkState.Connected
                : BluetoothAudioLinkState.Disconnected;
        }

        return BluetoothAudioRequestResult.Accepted;
    }
}
