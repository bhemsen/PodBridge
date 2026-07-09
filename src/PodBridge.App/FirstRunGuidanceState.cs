using System.IO;

namespace PodBridge.App;

/// <summary>
/// Persists whether the one-time "pair your AirPods" guidance notification has
/// already been shown, so it appears once per user and never again. Backed by a
/// marker file under <c>%LOCALAPPDATA%\PodBridge</c>. All file errors are treated
/// as "not yet shown" / best-effort so guidance never crashes the app.
/// </summary>
public sealed class FirstRunGuidanceState
{
    private readonly string _markerPath;

    /// <summary>Uses the default per-user marker under <c>%LOCALAPPDATA%\PodBridge</c>.</summary>
    public FirstRunGuidanceState()
        : this(DefaultMarkerPath())
    {
    }

    /// <summary>Testing seam: use an explicit marker path.</summary>
    public FirstRunGuidanceState(string markerPath) => _markerPath = markerPath;

    /// <summary>True once the guidance notification has been shown at least once.</summary>
    public bool HasBeenShown
    {
        get
        {
            try
            {
                return File.Exists(_markerPath);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <summary>Records that the guidance has now been shown (best-effort).</summary>
    public void MarkShown()
    {
        try
        {
            var directory = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_markerPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot persist the flag, guidance may show again
            // on the next run, but the app must never fail because of it.
        }
    }

    private static string DefaultMarkerPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PodBridge", "first-run-guidance.marker");
    }
}
