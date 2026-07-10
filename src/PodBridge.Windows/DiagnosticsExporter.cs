using System.Globalization;
using PodBridge.Core.Diagnostics;
using PodBridge.Windows.Interop;

namespace PodBridge.Windows;

/// <summary>
/// <see cref="IDiagnosticsExporter"/> adapter: renders the snapshot with Core's
/// <see cref="DiagnosticsSnapshotFormatter"/> and writes it to a timestamped file under
/// <c>%LOCALAPPDATA%\PodBridge\diagnostics</c> (constitution: local-only). It touches only
/// the local filesystem — no network-capable type is referenced anywhere in this type,
/// which <c>DiagnosticsExporterTests</c> asserts by reflection.
/// </summary>
public sealed class DiagnosticsExporter : IDiagnosticsExporter
{
    private readonly IDiagnosticsFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;

    /// <summary>Production constructor: writes under the real per-user local app-data folder.</summary>
    public DiagnosticsExporter()
        : this(new DefaultDiagnosticsFileSystem(), TimeProvider.System)
    {
    }

    // Test seam: PodBridge.Windows.Tests substitutes a fake filesystem + a fixed clock so the
    // rendered content and filename are exercised with no real disk write.
    internal DiagnosticsExporter(IDiagnosticsFileSystem fileSystem, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DiagnosticsExportResult Export(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var now = _timeProvider.GetLocalNow();
        var text = DiagnosticsSnapshotFormatter.Render(snapshot, now);
        var fileName = $"podbridge-diagnostics-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt";
        var path = Path.Combine(_fileSystem.GetExportDirectory(), fileName);
        _fileSystem.WriteAllText(path, text);
        return new DiagnosticsExportResult(path, text);
    }
}
