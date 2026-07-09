using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace PodBridge.App;

/// <summary>
/// Owns the system-tray icon and its context menu: a status line, a battery line,
/// "Pair / Reconnect", "Open Bluetooth settings", and "Exit". The status line is
/// driven live from <c>IConnectionMonitor</c> via <see cref="TrayStatusController"/>
/// (<see cref="SetStatus"/>); the battery line from <c>IDeviceStateProvider</c> via
/// <see cref="TrayBatteryController"/> (<see cref="SetBattery"/>). The tooltip is
/// composed from both. First-run pairing guidance is surfaced through
/// <see cref="ShowNotification"/>. Disposing removes the icon from the notification
/// area. Must be used on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string BluetoothSettingsUri = "ms-settings:bluetooth";
    private const string Placeholder = "—";

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _statusItem;
    private readonly MenuItem _batteryItem;

    private string _statusText = Placeholder;
    private string _batteryText = Placeholder;

    private TrayIcon()
    {
        _statusItem = new MenuItem { Header = $"Status: {Placeholder}", IsEnabled = false };
        _batteryItem = new MenuItem { Header = $"Battery: {Placeholder}", IsEnabled = false };
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
    /// Shows a Windows balloon/toast notification from the tray icon. Used for
    /// one-time first-run pairing guidance. Call on the UI thread.
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
        menu.Items.Add(new Separator());
        // Phase 1: "Pair / Reconnect" deep-links to Bluetooth settings like
        // "Open Bluetooth settings"; issue #7 gives it live reconnect behaviour.
        menu.Items.Add(CreateItem("Pair / Reconnect", OnOpenBluetoothSettings));
        menu.Items.Add(CreateItem("Open Bluetooth settings", OnOpenBluetoothSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Exit", OnExit));
        return menu;
    }

    private static MenuItem CreateItem(string header, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    private static void OnOpenBluetoothSettings(object sender, RoutedEventArgs e)
        => OpenBluetoothSettings();

    private static void OnExit(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private static void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo(BluetoothSettingsUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                "Could not open Windows Bluetooth settings.",
                "PodBridge",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
