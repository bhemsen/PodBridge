using System.IO;
using PodBridge.Core.Audio;

namespace PodBridge.App;

/// <summary>
/// Persists the user's selected <see cref="MicPolicyMode"/> across restarts, backed by
/// a small per-user text file under <c>%LOCALAPPDATA%\PodBridge</c> (local-only, no
/// network — constitution). The default on first run, and on any read error or
/// unrecognised value, is <see cref="MicPolicyMode.HiFiLock"/> (spec: "great by
/// default"). All file access is best-effort so a settings read or write never crashes
/// the tray app. Only the mode is persisted; the Call-mode toggle deliberately defaults
/// off each launch so the AirPods are never silently forced into HFP/mono at startup.
/// </summary>
public sealed class MicPolicyModeStore
{
    private readonly string _settingsPath;

    /// <summary>Uses the default per-user settings file under <c>%LOCALAPPDATA%\PodBridge</c>.</summary>
    public MicPolicyModeStore()
        : this(DefaultSettingsPath())
    {
    }

    /// <summary>Testing seam: use an explicit settings-file path.</summary>
    public MicPolicyModeStore(string settingsPath) => _settingsPath = settingsPath;

    /// <summary>
    /// Reads the persisted mode, defaulting to <see cref="MicPolicyMode.HiFiLock"/> on a
    /// missing file, an I/O error, or any value that is not a defined mode.
    /// </summary>
    public MicPolicyMode Load()
    {
        try
        {
            var text = File.ReadAllText(_settingsPath).Trim();
            return Enum.TryParse<MicPolicyMode>(text, ignoreCase: true, out var mode)
                && Enum.IsDefined(mode)
                    ? mode
                    : MicPolicyMode.HiFiLock;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return MicPolicyMode.HiFiLock;
        }
    }

    /// <summary>Persists <paramref name="mode"/> as its enum name (best-effort).</summary>
    public void Save(MicPolicyMode mode)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, mode.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot persist the mode it may not survive a restart,
            // but the app must never fail because of it.
        }
    }

    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PodBridge", "mic-policy-mode.txt");
    }
}
