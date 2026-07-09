using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace PodBridge.App;

/// <summary>
/// Owns the system-tray icon and its context menu: a status line,
/// "Pair / Reconnect", "Open Bluetooth settings", and "Exit". The status line
/// and tooltip are driven live from <c>IConnectionMonitor</c> via
/// <see cref="TrayStatusController"/> (<see cref="SetStatus"/>); first-run
/// pairing guidance is surfaced through <see cref="ShowNotification"/>. Disposing
/// removes the icon from the notification area. Must be used on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string BluetoothSettingsUri = "ms-settings:bluetooth";

    private readonly TaskbarIcon _icon;
    private readonly MenuItem _statusItem;

    private TrayIcon()
    {
        _statusItem = new MenuItem { Header = "Status: —", IsEnabled = false };
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
    /// Updates the context-menu status line and the icon tooltip from the given
    /// display phrase (see <c>ConnectionStatusText</c>). Call on the UI thread.
    /// </summary>
    public void SetStatus(string statusText)
    {
        _statusItem.Header = $"Status: {statusText}";
        _icon.ToolTipText = $"PodBridge — {statusText}";
    }

    /// <summary>
    /// Shows a Windows balloon/toast notification from the tray icon. Used for
    /// one-time first-run pairing guidance. Call on the UI thread.
    /// </summary>
    public void ShowNotification(string title, string message)
        => _icon.ShowNotification(title, message, NotificationIcon.Info);

    /// <summary>Removes the icon from the notification area.</summary>
    public void Dispose() => _icon.Dispose();

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(_statusItem);
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
