using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using PodBridge.Core.Audio;
using PodBridge.Core.Branding;
using PodBridge.Core.Models;

namespace PodBridge.App;

/// <summary>
/// Owns the system-tray icon and its context menu: a status line, a battery line,
/// a codec line, a mic-mode line, "Refresh audio status", "Pair / Reconnect",
/// "Open Bluetooth settings", and "Exit". The status line is driven live from
/// <c>IConnectionMonitor</c> via <see cref="TrayStatusController"/>
/// (<see cref="SetStatus"/>); the battery line from <c>IDeviceStateProvider</c> via
/// <see cref="TrayBatteryController"/> (<see cref="SetBattery"/>); the codec and
/// mic-mode lines from the read-only audio reader via <see cref="TrayAudioController"/>
/// (<see cref="SetCodec"/>/<see cref="SetMicMode"/>, with the on-demand read wired via
/// <see cref="SetRefreshAudioHandler"/>). A "Microphone mode" submenu exposes the three
/// mic-profile policy modes (HiFi-lock / Auto-switch / Call-mode) as a radio group plus
/// a Call-mode toggle and the honest single-device degrade warning line, driven by
/// <see cref="TrayMicController"/> (<see cref="SetMicPolicyHandlers"/>,
/// <see cref="SetSelectedMicMode"/>, <see cref="SetCallModeActive"/>,
/// <see cref="SetMicWarning"/>). A "Noise control" submenu (Tier 2, opt-in) exposes the
/// AAP noise-control modes (Off / Noise Cancellation / Transparency / Adaptive) as a
/// radio group driven by <see cref="TrayNoiseControlController"/>
/// (<see cref="SetNoiseControlModeHandler"/>, <see cref="SetSelectedNoiseControlMode"/>,
/// <see cref="SetNoiseControlAvailability"/>); when the optional advanced-tier driver is
/// absent the modes are disabled and an honest explanation plus an "Enable advanced
/// tier…" affordance (wired via <see cref="SetEnableAdvancedTierHandler"/> to the honest
/// warning + explicit elevated install step; falls back to the docs) are shown instead —
/// never silently broken. A "Gesture controls…" entry opens the Tier-2 gesture-remap
/// settings window via the handler wired with <see cref="SetGestureSettingsHandler"/>
/// (the window surfaces the driver-absent / unsupported-model states honestly). An
/// "About PodBridge" entry opens the About window via
/// the handler wired with <see cref="SetAboutHandler"/>. The tooltip stays concise
/// (status + battery only); the audio surface lives in the menu. First-run pairing
/// guidance and the SBC guidance are surfaced through <see cref="ShowNotification"/>.
/// Disposing removes the icon from the notification area. Must be used on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string BluetoothSettingsUri = "ms-settings:bluetooth";
    private const string Placeholder = "—";

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _statusItem;
    private readonly MenuItem _batteryItem;
    private readonly MenuItem _codecItem;
    private readonly MenuItem _micItem;
    private readonly MenuItem _micPolicyMenu;
    private readonly MenuItem _hifiLockItem;
    private readonly MenuItem _autoSwitchItem;
    private readonly MenuItem _callModeModeItem;
    private readonly MenuItem _callModeToggleItem;
    private readonly MenuItem _micWarningItem;
    private readonly MenuItem _noiseControlMenu;
    private readonly MenuItem _ncOffItem;
    private readonly MenuItem _ncAncItem;
    private readonly MenuItem _ncTransparencyItem;
    private readonly MenuItem _ncAdaptiveItem;
    private readonly Separator _ncUnavailableSeparator;
    private readonly MenuItem _ncUnavailableItem;
    private readonly MenuItem _ncEnableTierItem;

    private string _statusText = Placeholder;
    private string _batteryText = Placeholder;
    private Action? _refreshAudioHandler;
    private Action<MicPolicyMode>? _micModeHandler;
    private Action? _callModeToggleHandler;
    private Action<NoiseControlMode>? _noiseControlModeHandler;
    private Action? _enableAdvancedTierHandler;
    private Action? _gestureSettingsHandler;
    private Action? _aboutHandler;

    private TrayIcon()
    {
        _statusItem = new MenuItem { Header = $"Status: {Placeholder}", IsEnabled = false };
        _batteryItem = new MenuItem { Header = $"Battery: {Placeholder}", IsEnabled = false };
        _codecItem = new MenuItem { Header = $"Codec: {Placeholder}", IsEnabled = false };
        _micItem = new MenuItem { Header = $"Mic: {Placeholder}", IsEnabled = false };
        _hifiLockItem = CreateMicModeItem("HiFi-lock", MicPolicyMode.HiFiLock);
        _autoSwitchItem = CreateMicModeItem("Auto-switch", MicPolicyMode.AutoSwitch);
        _callModeModeItem = CreateMicModeItem("Call-mode", MicPolicyMode.CallMode);
        _callModeToggleItem = new MenuItem { Header = "AirPods mic (Call-mode)", IsCheckable = true };
        _callModeToggleItem.Click += OnCallModeToggle;
        _micWarningItem = new MenuItem { IsEnabled = false, Visibility = Visibility.Collapsed };
        _micPolicyMenu = BuildMicPolicyMenu();
        _ncOffItem = CreateNoiseControlItem("Off", NoiseControlMode.Off);
        _ncAncItem = CreateNoiseControlItem("Noise Cancellation", NoiseControlMode.NoiseCancellation);
        _ncTransparencyItem = CreateNoiseControlItem("Transparency", NoiseControlMode.Transparency);
        _ncAdaptiveItem = CreateNoiseControlItem("Adaptive", NoiseControlMode.Adaptive);
        _ncUnavailableSeparator = new Separator { Visibility = Visibility.Collapsed };
        _ncUnavailableItem = new MenuItem { IsEnabled = false, Visibility = Visibility.Collapsed };
        _ncEnableTierItem = new MenuItem { Header = "Enable advanced tier…", Visibility = Visibility.Collapsed };
        _ncEnableTierItem.Click += OnEnableAdvancedTier;
        _noiseControlMenu = BuildNoiseControlMenu();
        _icon = new TaskbarIcon
        {
            ToolTipText = "PodBridge",
            IconSource = new BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute)),
            ContextMenu = BuildContextMenu(),
        };
        _icon.ForceCreate();
    }

    /// <summary>Creates the tray icon and shows it in the notification area.</summary>
    public static TrayIcon Create() => new();

    /// <summary>
    /// Updates the context-menu status line and refreshes the tooltip from the given
    /// display phrase (see <c>ConnectionStatusText</c>). Call on the UI thread.
    /// </summary>
    public void SetStatus(string statusText)
    {
        _statusText = statusText;
        _statusItem.Header = $"Status: {statusText}";
        UpdateToolTip();
    }

    /// <summary>
    /// Updates the context-menu battery line and refreshes the tooltip from the
    /// given display phrase (see <c>BatteryStatusText</c>) — left/right/case % with
    /// charging, or "unknown / out of range". Call on the UI thread.
    /// </summary>
    public void SetBattery(string batteryText)
    {
        _batteryText = batteryText;
        _batteryItem.Header = $"Battery: {batteryText}";
        UpdateToolTip();
    }

    /// <summary>
    /// Updates the context-menu codec line from the guidance engine's ready-to-render
    /// phrase (e.g. "Codec: AAC (best available on Windows)" / "Codec: SBC" /
    /// "Codec: couldn't determine"). The phrase already carries its own label, so it
    /// is set verbatim. Call on the UI thread.
    /// </summary>
    public void SetCodec(string codecLine) => _codecItem.Header = codecLine;

    /// <summary>
    /// Updates the context-menu mic-mode line from the guidance engine's phrase (e.g.
    /// "Mic: High quality (A2DP)" / "Mic: Call mode (mono)" / "Mic: couldn't
    /// determine"), set verbatim. Call on the UI thread.
    /// </summary>
    public void SetMicMode(string micLine) => _micItem.Header = micLine;

    /// <summary>
    /// Wires the callback invoked by the "Refresh audio status" menu action (an
    /// on-demand audio re-read). Call on the UI thread.
    /// </summary>
    public void SetRefreshAudioHandler(Action handler) => _refreshAudioHandler = handler;

    /// <summary>
    /// Wires the callback invoked by the "About PodBridge" menu action (opens the
    /// About window). Call on the UI thread.
    /// </summary>
    public void SetAboutHandler(Action handler) => _aboutHandler = handler;

    /// <summary>
    /// Wires the callback invoked by the "Gesture controls…" menu action (opens the Tier-2
    /// gesture-remap settings window). The window itself surfaces the driver-absent /
    /// unsupported-model states honestly, so the entry is always shown. Call on the UI thread.
    /// </summary>
    public void SetGestureSettingsHandler(Action handler) => _gestureSettingsHandler = handler;

    /// <summary>
    /// Wires the mic-policy submenu actions: <paramref name="onModeSelected"/> fires
    /// with the picked mode, <paramref name="onCallModeToggled"/> when the Call-mode
    /// toggle is clicked. Call on the UI thread.
    /// </summary>
    public void SetMicPolicyHandlers(Action<MicPolicyMode> onModeSelected, Action onCallModeToggled)
    {
        _micModeHandler = onModeSelected;
        _callModeToggleHandler = onCallModeToggled;
    }

    /// <summary>
    /// Checks exactly the submenu item for <paramref name="mode"/> (radio behaviour), so
    /// the menu always reflects the engine's current mode. Call on the UI thread.
    /// </summary>
    public void SetSelectedMicMode(MicPolicyMode mode)
    {
        _hifiLockItem.IsChecked = mode == MicPolicyMode.HiFiLock;
        _autoSwitchItem.IsChecked = mode == MicPolicyMode.AutoSwitch;
        _callModeModeItem.IsChecked = mode == MicPolicyMode.CallMode;
    }

    /// <summary>
    /// Reflects the Call-mode toggle (whether the AirPods currently hold the
    /// communications role) as the toggle item's check. Call on the UI thread.
    /// </summary>
    public void SetCallModeActive(bool active) => _callModeToggleItem.IsChecked = active;

    /// <summary>
    /// Shows or hides the honest single-device degrade warning line with
    /// <paramref name="warningText"/> (e.g. "no alternate mic — AirPods mic requires
    /// HFP/mono"). Call on the UI thread.
    /// </summary>
    public void SetMicWarning(bool degraded, string warningText)
    {
        _micWarningItem.Header = warningText;
        _micWarningItem.Visibility = degraded ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Wires the callback invoked when a noise-control mode is picked from the "Noise
    /// control" submenu. The controller applies it optimistically and confirms/reverts
    /// on the device echo. Call on the UI thread.
    /// </summary>
    public void SetNoiseControlModeHandler(Action<NoiseControlMode> onModeSelected)
        => _noiseControlModeHandler = onModeSelected;

    /// <summary>
    /// Wires the callback invoked by the "Enable advanced tier…" affordance shown when the
    /// optional advanced-tier driver is absent. The handler owns the honest warning and the
    /// explicit, user-triggered elevated install step (the app stays <c>asInvoker</c>). If no
    /// handler is wired, the item falls back to opening the advanced-tier documentation. Call
    /// on the UI thread.
    /// </summary>
    public void SetEnableAdvancedTierHandler(Action handler) => _enableAdvancedTierHandler = handler;

    /// <summary>
    /// Checks exactly the submenu item for <paramref name="mode"/> (radio behaviour), or
    /// clears all checks when <paramref name="mode"/> is <see langword="null"/> (unknown /
    /// not yet read). Reflects the optimistic and confirmed/reverted mode. Call on the UI
    /// thread.
    /// </summary>
    public void SetSelectedNoiseControlMode(NoiseControlMode? mode)
    {
        _ncOffItem.IsChecked = mode == NoiseControlMode.Off;
        _ncAncItem.IsChecked = mode == NoiseControlMode.NoiseCancellation;
        _ncTransparencyItem.IsChecked = mode == NoiseControlMode.Transparency;
        _ncAdaptiveItem.IsChecked = mode == NoiseControlMode.Adaptive;
    }

    /// <summary>
    /// Reflects the Tier-2 transport availability in the submenu (constitution: graceful
    /// degradation, honest UX). When <paramref name="available"/>, the modes are
    /// selectable and Adaptive is enabled only if <paramref name="adaptiveSupported"/>
    /// (model gate). When not available, all modes are disabled and an honest
    /// explanation (<paramref name="unavailableText"/>) plus the "Enable advanced tier…"
    /// affordance are shown instead. Call on the UI thread.
    /// </summary>
    public void SetNoiseControlAvailability(bool available, bool adaptiveSupported, string unavailableText)
    {
        _ncOffItem.IsEnabled = available;
        _ncAncItem.IsEnabled = available;
        _ncTransparencyItem.IsEnabled = available;
        // Adaptive is additionally gated on the connected model (Pro 2); the rest are not.
        _ncAdaptiveItem.IsEnabled = available && adaptiveSupported;

        var absent = available ? Visibility.Collapsed : Visibility.Visible;
        _ncUnavailableItem.Header = unavailableText;
        _ncUnavailableSeparator.Visibility = absent;
        _ncUnavailableItem.Visibility = absent;
        _ncEnableTierItem.Visibility = absent;
    }

    /// <summary>
    /// Shows a Windows balloon/toast notification from the tray icon. Used for
    /// one-time first-run pairing guidance and the confirmed-SBC audio guidance. Call
    /// on the UI thread.
    /// </summary>
    public void ShowNotification(string title, string message)
        => _icon.ShowNotification(title, message, NotificationIcon.Info);

    /// <summary>Removes the icon from the notification area.</summary>
    public void Dispose() => _icon.Dispose();

    // Tooltip carries both the connection status and the battery line so the two
    // controllers can update independently without clobbering each other.
    private void UpdateToolTip()
        => _icon.ToolTipText = $"PodBridge — {_statusText} · {_batteryText}";

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_batteryItem);
        menu.Items.Add(_codecItem);
        menu.Items.Add(_micItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_micPolicyMenu);
        menu.Items.Add(_noiseControlMenu);
        menu.Items.Add(CreateItem("Refresh audio status", OnRefreshAudio));
        // Phase 1: "Pair / Reconnect" deep-links to Bluetooth settings like
        // "Open Bluetooth settings"; issue #7 gives it live reconnect behaviour.
        menu.Items.Add(CreateItem("Pair / Reconnect", OnOpenBluetoothSettings));
        menu.Items.Add(CreateItem("Open Bluetooth settings", OnOpenBluetoothSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Gesture controls…", OnGestureSettings));
        menu.Items.Add(CreateItem("About PodBridge", OnAbout));
        menu.Items.Add(CreateItem("Exit", OnExit));
        return menu;
    }

    private static MenuItem CreateItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    // The "Microphone mode" submenu: three checkable modes (radio), a Call-mode toggle,
    // and a collapsed degrade-warning line the controller reveals when there is no
    // non-AirPods fallback mic.
    private MenuItem BuildMicPolicyMenu()
    {
        var menu = new MenuItem { Header = "Microphone mode" };
        menu.Items.Add(_hifiLockItem);
        menu.Items.Add(_autoSwitchItem);
        menu.Items.Add(_callModeModeItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_callModeToggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(_micWarningItem);
        return menu;
    }

    private MenuItem CreateMicModeItem(string header, MicPolicyMode mode)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        // The controller re-asserts the checks from the engine's current mode, so a
        // click on the already-checked item can never leave the radio group empty.
        item.Click += (_, _) => _micModeHandler?.Invoke(mode);
        return item;
    }

    // The "Noise control" submenu (Tier 2, opt-in): four checkable modes (radio) driven
    // by the device echo, then a collapsed driver-absent block — an honest explanation
    // line plus an "Enable advanced tier…" affordance — that the controller reveals when
    // the transport is unavailable (constitution: graceful degradation, never silent).
    private MenuItem BuildNoiseControlMenu()
    {
        var menu = new MenuItem { Header = "Noise control" };
        menu.Items.Add(_ncOffItem);
        menu.Items.Add(_ncAncItem);
        menu.Items.Add(_ncTransparencyItem);
        menu.Items.Add(_ncAdaptiveItem);
        menu.Items.Add(_ncUnavailableSeparator);
        menu.Items.Add(_ncUnavailableItem);
        menu.Items.Add(_ncEnableTierItem);
        return menu;
    }

    private MenuItem CreateNoiseControlItem(string header, NoiseControlMode mode)
    {
        var item = new MenuItem { Header = header, IsCheckable = true };
        // The controller re-asserts the checks from the confirmed/optimistic mode, so a
        // click never leaves the radio group in a state the device did not confirm.
        item.Click += (_, _) => _noiseControlModeHandler?.Invoke(mode);
        return item;
    }

    // The advanced tier ships as a separate, user-triggered install (its own driver INF
    // + test-cert trust — see docs/specs/spec-advanced-driver-anc.md). The wired handler
    // (App) shows the honest security warning and launches the ELEVATED install step on
    // explicit confirmation; the app itself stays asInvoker and never auto-elevates. With
    // no handler wired, the item falls back to opening the advanced-tier docs — it never
    // elevates or installs anything on its own.
    private void OnEnableAdvancedTier(object sender, RoutedEventArgs e)
    {
        if (_enableAdvancedTierHandler is not null)
        {
            _enableAdvancedTierHandler();
            return;
        }

        OpenUri(ProductInfo.AdvancedTierDocsUrl, "Could not open the PodBridge documentation.");
    }

    private void OnCallModeToggle(object sender, RoutedEventArgs e)
        => _callModeToggleHandler?.Invoke();

    private void OnRefreshAudio(object sender, RoutedEventArgs e)
        => _refreshAudioHandler?.Invoke();

    private void OnGestureSettings(object sender, RoutedEventArgs e)
        => _gestureSettingsHandler?.Invoke();

    private void OnAbout(object sender, RoutedEventArgs e)
        => _aboutHandler?.Invoke();

    private static void OnOpenBluetoothSettings(object sender, RoutedEventArgs e)
        => OpenUri(BluetoothSettingsUri, "Could not open Windows Bluetooth settings.");

    private static void OnExit(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    // Shell-launches a URI (a Bluetooth-settings deep link or an https docs page),
    // surfacing a non-fatal warning if the shell cannot handle it. Used for the
    // "Open Bluetooth settings" / "Pair / Reconnect" entries and the noise-control
    // "Enable advanced tier…" affordance.
    private static void OpenUri(string uri, string failMessage)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(failMessage, "PodBridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
