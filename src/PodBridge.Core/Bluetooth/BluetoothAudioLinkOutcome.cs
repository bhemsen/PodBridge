namespace PodBridge.Core.Bluetooth;

/// <summary>The final outcome of a <see cref="BluetoothAudioLinkController"/> connect or disconnect.</summary>
public enum BluetoothAudioLinkOutcome
{
    /// <summary>The link reached the requested state (confirmed by the link state, not the request).</summary>
    Succeeded = 0,

    /// <summary>The link was already in the requested state; nothing was sent.</summary>
    AlreadyInState = 1,

    /// <summary>No paired AirPods were found; pairing happens in Windows Bluetooth settings.</summary>
    NotFound = 2,

    /// <summary>The AirPods expose no Bluetooth audio connection control.</summary>
    Unsupported = 3,

    /// <summary>The Bluetooth audio driver rejected the request.</summary>
    Rejected = 4,

    /// <summary>The request was accepted but the link did not change before the timeout.</summary>
    TimedOut = 5,

    /// <summary>Another connect/disconnect is already in progress; nothing was sent.</summary>
    Busy = 6,
}
