namespace PodBridge.Core.Startup;

/// <summary>
/// The device-independent state of the opt-in "start at sign-in" auto-start task,
/// mirroring the four MSIX <c>StartupTask</c> states without any WinRT dependency
/// (Core stays OS-free). The default is <see cref="Disabled"/> — auto-start is
/// opt-in and off until the user turns it on from the About surface.
/// </summary>
public enum StartupToggleState
{
    /// <summary>Auto-start is off (the packaged default, <c>Enabled="false"</c>).</summary>
    Disabled,

    /// <summary>
    /// The user turned auto-start off in Windows Settings / Task Manager. The app
    /// cannot re-enable it; a request is honestly reflected as still-disabled.
    /// </summary>
    DisabledByUser,

    /// <summary>Group policy blocks auto-start; the app cannot enable it.</summary>
    DisabledByPolicy,

    /// <summary>Auto-start is on — the app launches at user sign-in.</summary>
    Enabled,
}
