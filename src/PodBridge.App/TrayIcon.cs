using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace PodBridge.App;

/// <summary>
/// Owns the system-tray icon and its context menu: a status line,
/// "Pair / Reconnect", "Open Bluetooth settings", and "Exit". The status line is
/// a static Phase-1 placeholder — live connection status and first-run pairing
/// guidance are wired in issue #7 via <c>IConnectionMonitor</c>. Disposing
/// removes the icon from the notification area.
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
    /// Sets the placeholder status line. Issue #7 drives this from live
    /// connection state; Phase 1 leaves it at its static default.
    /// </summary>
    public void SetStatus(string status) => _statusItem.Header = status;

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
