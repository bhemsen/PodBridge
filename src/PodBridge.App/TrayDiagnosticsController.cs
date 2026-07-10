using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using PodBridge.Core.Diagnostics;
using PodBridge.Windows.Logging;

namespace PodBridge.App;

/// <summary>
/// Wires the tray "Export diagnostics" action and the "Debug logging" toggle
/// (issue #54). Export builds a fresh <see cref="DiagnosticsSnapshot"/> from the live Core
/// state (<see cref="IDiagnosticsSnapshotFactory"/>), writes it to a local file via
/// <see cref="IDiagnosticsExporter"/>, and copies the same text to the clipboard — the
/// clipboard copy lives here (not in the Windows adapter) so the adapter stays free of a
/// UI-framework dependency (see <see cref="IDiagnosticsExporter"/>'s note). The Debug
/// toggle flips the local <see cref="RollingFileLoggerProvider"/>'s <see cref="RollingFileLoggerProvider.MinLevel"/>
/// at runtime — the local file sink only, never a network effect (constitution: local-only).
/// Owns no subscription; it only wires tray handlers, so it needs no <see cref="IDisposable"/>.
/// </summary>
public sealed class TrayDiagnosticsController
{
    private const string ExportedTitle = "Diagnostics exported";
    private const string FailedTitle = "Diagnostics export failed";

    private readonly TrayIcon _tray;
    private readonly IDiagnosticsSnapshotFactory _factory;
    private readonly IDiagnosticsExporter _exporter;
    private readonly RollingFileLoggerProvider _logProvider;
    private readonly Dispatcher _dispatcher;

    private TrayDiagnosticsController(
        TrayIcon tray,
        IDiagnosticsSnapshotFactory factory,
        IDiagnosticsExporter exporter,
        RollingFileLoggerProvider logProvider,
        Dispatcher dispatcher)
    {
        _tray = tray;
        _factory = factory;
        _exporter = exporter;
        _logProvider = logProvider;
        _dispatcher = dispatcher;
    }

    /// <summary>Creates the controller binding the diagnostics services to the tray.</summary>
    public static TrayDiagnosticsController Create(
        TrayIcon tray,
        IDiagnosticsSnapshotFactory factory,
        IDiagnosticsExporter exporter,
        RollingFileLoggerProvider logProvider,
        Dispatcher dispatcher)
        => new(tray, factory, exporter, logProvider, dispatcher);

    /// <summary>
    /// Wires the tray handlers and reflects the current Debug-logging state. Must be
    /// called on the UI thread.
    /// </summary>
    public void Start()
    {
        _tray.SetDiagnosticsHandlers(ExportDiagnostics, SetDebugLogging);
        _tray.SetDebugLoggingChecked(_logProvider.MinLevel <= LogLevel.Debug);
    }

    // Builds + writes the snapshot and copies it to the clipboard. Best-effort: a
    // filesystem/clipboard failure surfaces an honest notification and never crashes the
    // tray app (constitution: graceful degradation).
    private void ExportDiagnostics()
    {
        try
        {
            var result = _exporter.Export(_factory.Create());
            TrySetClipboard(result.Text);
            _tray.ShowNotification(
                ExportedTitle, $"Saved to {result.FilePath} and copied to the clipboard.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _tray.ShowNotification(FailedTitle, "Could not write the diagnostics file.");
        }
    }

    // The clipboard is UI-thread-affine and can transiently fail if another app holds it
    // open; the file write already succeeded, so a clipboard miss is non-fatal.
    private void TrySetClipboard(string text)
    {
        _dispatcher.Invoke(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Clipboard busy: the file on disk is still the complete diagnostics.
            }
        });
    }

    // Raises/lowers the local file sink's verbosity at runtime — Debug when opted in,
    // Information otherwise. No restart, no network effect (local file only).
    private void SetDebugLogging(bool enabled)
        => _logProvider.MinLevel = enabled ? LogLevel.Debug : LogLevel.Information;
}
