namespace PodBridge.Core.Diagnostics;

/// <summary>
/// The path a snapshot was written to plus the exact rendered text, so the caller (the
/// tray "Export diagnostics" action) copies the <b>same</b> text to the clipboard that was
/// written to disk rather than re-rendering it a second time.
/// </summary>
public sealed record DiagnosticsExportResult(string FilePath, string Text);

/// <summary>
/// Writes a <see cref="DiagnosticsSnapshot"/> to a local file — the OS boundary for the
/// tray "Export diagnostics" action. No network call is ever made (constitution:
/// local-only). Implemented in <c>PodBridge.Windows</c> (<c>DiagnosticsExporter</c>), which
/// only touches the local filesystem (no P/Invoke needed); the App's tray handler copies
/// the returned <see cref="DiagnosticsExportResult.Text"/> to the clipboard via WPF's own
/// <c>System.Windows.Clipboard</c> — that keeps the Windows adapter free of a UI-framework
/// dependency (architecture.md: "No UI here") while the end-to-end action still writes a
/// file <b>and</b> copies to clipboard.
/// </summary>
public interface IDiagnosticsExporter
{
    /// <summary>Renders and writes <paramref name="snapshot"/> to a local file.</summary>
    DiagnosticsExportResult Export(DiagnosticsSnapshot snapshot);
}
