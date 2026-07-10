namespace PodBridge.Windows.Interop;

// Filesystem seam for DiagnosticsExporter (issue #54). DiagnosticsExporter reaches the OS
// ONLY through the interface below; PodBridge.Windows.Tests substitutes a fake so the
// export decision logic (directory, filename, written content) is exercised device- and
// filesystem-independently, and so a test can prove the exporter never performs a network
// call (it depends on no network-capable type at all).

/// <summary>
/// The local directory the exporter writes into, plus the actual write. Kept minimal and
/// synchronous — diagnostics export is a rare, user-triggered, small write.
/// </summary>
internal interface IDiagnosticsFileSystem
{
    /// <summary>The directory diagnostics exports are written to (created if missing).</summary>
    string GetExportDirectory();

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/> as UTF-8 text.</summary>
    void WriteAllText(string path, string content);
}

/// <summary>
/// Real filesystem: <c>%LOCALAPPDATA%\PodBridge\diagnostics</c> (local-only, no network —
/// constitution), created on first export.
/// </summary>
internal sealed class DefaultDiagnosticsFileSystem : IDiagnosticsFileSystem
{
    public string GetExportDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "PodBridge", "diagnostics");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
}
