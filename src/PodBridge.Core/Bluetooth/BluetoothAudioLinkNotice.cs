namespace PodBridge.Core.Bluetooth;

/// <summary>
/// A user-facing result of a one-click connect/disconnect: the notification to show and
/// whether the tray should fall back to opening Windows Bluetooth settings.
/// </summary>
/// <param name="Title">Short notification title.</param>
/// <param name="Message">Honest notification body.</param>
/// <param name="OpenBluetoothSettings">True when the action failed or found nothing to act on,
/// so the user is sent to Windows Bluetooth settings to finish it there.</param>
public sealed record BluetoothAudioLinkNotice(string Title, string Message, bool OpenBluetoothSettings);
