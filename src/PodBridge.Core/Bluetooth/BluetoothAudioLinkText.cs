using PodBridge.Core.Models;

namespace PodBridge.Core.Bluetooth;

/// <summary>
/// Pure, device-independent presentation for the tray's one-click "Connect AirPods" /
/// "Disconnect AirPods" actions: the menu labels, when each action is offered for a given
/// <see cref="ConnectionStatus"/>, and the honest notification (plus the Bluetooth-settings
/// fallback decision) for every <see cref="BluetoothAudioLinkOutcome"/>. Kept in Core so the
/// Tier-1 test gate covers it (<c>BluetoothAudioLinkTextTests</c>).
/// </summary>
public static class BluetoothAudioLinkText
{
    /// <summary>Tray menu label for the connect action.</summary>
    public const string ConnectLabel = "Connect AirPods";

    /// <summary>Tray menu label shown (disabled) while a connect is in progress.</summary>
    public const string ConnectingLabel = "Connecting…";

    /// <summary>Tray menu label for the disconnect action.</summary>
    public const string DisconnectLabel = "Disconnect AirPods";

    /// <summary>Tray menu label shown (disabled) while a disconnect is in progress.</summary>
    public const string DisconnectingLabel = "Disconnecting…";

    private const string SettingsFallback = " Opening Windows Bluetooth settings.";

    /// <summary>
    /// Connect is offered unless the AirPods are known to be connected. With no AirPods
    /// paired it stays offered and falls back to Bluetooth settings, the pairing path.
    /// </summary>
    public static bool CanConnect(ConnectionStatus status) => status != ConnectionStatus.Connected;

    /// <summary>Disconnect is offered when the AirPods are connected (or the status is not yet known).</summary>
    public static bool CanDisconnect(ConnectionStatus status)
        => status is ConnectionStatus.Connected or ConnectionStatus.Unknown;

    /// <summary>
    /// The notification for <paramref name="outcome"/> of <paramref name="request"/>, or
    /// <see langword="null"/> when nothing should be shown (<see cref="BluetoothAudioLinkOutcome.Busy"/>).
    /// </summary>
    public static BluetoothAudioLinkNotice? ForOutcome(
        BluetoothAudioRequest request, BluetoothAudioLinkOutcome outcome)
        => request == BluetoothAudioRequest.Connect ? ForConnect(outcome) : ForDisconnect(outcome);

    private static BluetoothAudioLinkNotice? ForConnect(BluetoothAudioLinkOutcome outcome) => outcome switch
    {
        BluetoothAudioLinkOutcome.Succeeded => Done("AirPods connected", "Your AirPods are connected to this PC."),
        BluetoothAudioLinkOutcome.AlreadyInState => Done("AirPods connected", "Your AirPods were already connected."),
        BluetoothAudioLinkOutcome.NotFound => NotFound(),
        BluetoothAudioLinkOutcome.Unsupported => Fallback(
            "Couldn't connect", "Windows offers no connect control for these AirPods."),
        BluetoothAudioLinkOutcome.Rejected => Fallback(
            "Couldn't connect", "Windows declined the connect request."),
        BluetoothAudioLinkOutcome.TimedOut => Fallback(
            "Couldn't connect",
            "Your AirPods didn't connect. Take them out of the case (or open the lid) and try again."),
        _ => null,
    };

    private static BluetoothAudioLinkNotice? ForDisconnect(BluetoothAudioLinkOutcome outcome) => outcome switch
    {
        BluetoothAudioLinkOutcome.Succeeded => Done(
            "AirPods disconnected",
            $"Your AirPods are disconnected from this PC and stay paired. Use \"{ConnectLabel}\" to reconnect."),
        BluetoothAudioLinkOutcome.AlreadyInState => Done(
            "AirPods disconnected", "Your AirPods were already disconnected."),
        BluetoothAudioLinkOutcome.NotFound => NotFound(),
        BluetoothAudioLinkOutcome.Unsupported => Fallback(
            "Couldn't disconnect", "Windows offers no disconnect control for these AirPods."),
        BluetoothAudioLinkOutcome.Rejected => Fallback(
            "Couldn't disconnect", "Windows declined the disconnect request."),
        BluetoothAudioLinkOutcome.TimedOut => Fallback(
            "Couldn't disconnect", "Your AirPods are still connected."),
        _ => null,
    };

    private static BluetoothAudioLinkNotice NotFound() => Fallback(
        "No paired AirPods found", "Pair your AirPods in Windows Bluetooth settings first.");

    private static BluetoothAudioLinkNotice Done(string title, string message) => new(title, message, false);

    private static BluetoothAudioLinkNotice Fallback(string title, string message)
        => new(title, message + SettingsFallback, true);
}
