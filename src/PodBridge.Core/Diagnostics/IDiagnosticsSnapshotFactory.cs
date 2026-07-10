namespace PodBridge.Core.Diagnostics;

/// <summary>
/// Reads the current, connection-gated device/audio/capability facts and assembles a
/// <see cref="DiagnosticsSnapshot"/>. The tray "Export diagnostics" action resolves this
/// plus <see cref="IDiagnosticsExporter"/>. Implemented by
/// <see cref="DiagnosticsSnapshotFactory"/>.
/// </summary>
public interface IDiagnosticsSnapshotFactory
{
    /// <summary>Builds a fresh snapshot from the current live state.</summary>
    DiagnosticsSnapshot Create();
}
