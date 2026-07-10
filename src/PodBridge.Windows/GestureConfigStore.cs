using System.IO;
using PodBridge.Core.Models;
using PodBridge.Core.Protocol;

namespace PodBridge.Windows;

/// <summary>
/// Persists the user's press-and-hold <see cref="GestureConfiguration"/> across restarts,
/// backed by a small per-user text file under <c>%LOCALAPPDATA%\PodBridge</c> (local-only,
/// no network — constitution). Mirrors the mic-policy store pattern. The stored value is the
/// two per-bud action names, <c>right;left</c> (e.g. <c>NoiseControl;Siri</c>).
/// <para>
/// The configuration is persisted because Apple firmware overwrites it on every reconnect,
/// so <see cref="GestureRepushController"/> must reload and re-send the user's choice
/// (docs/research/gesture-aap.md "reconnect-overwrite"). All file access is best-effort so a
/// settings read or write never crashes the tray app; a missing file, an I/O error, or any
/// unrecognised value reads back as <see langword="null"/> (no assignment yet), and the
/// re-push policy then sends nothing.
/// </para>
/// </summary>
public sealed class GestureConfigStore : IGestureConfigStore
{
    private const char Separator = ';';

    private readonly string _settingsPath;

    /// <summary>Uses the default per-user settings file under <c>%LOCALAPPDATA%\PodBridge</c>.</summary>
    public GestureConfigStore()
        : this(DefaultSettingsPath())
    {
    }

    /// <summary>Testing seam: use an explicit settings-file path.</summary>
    public GestureConfigStore(string settingsPath) => _settingsPath = settingsPath;

    /// <summary>
    /// Reads the persisted configuration, or <see langword="null"/> on a missing file, an
    /// I/O error, or any value that is not two defined <see cref="GestureAction"/> names.
    /// </summary>
    public GestureConfiguration? Load()
    {
        try
        {
            var parts = File.ReadAllText(_settingsPath).Trim().Split(Separator);
            if (parts.Length == 2
                && TryParseAction(parts[0], out var right)
                && TryParseAction(parts[1], out var left))
            {
                return new GestureConfiguration(right, left);
            }

            return null; // unrecognised value -> treated as "no assignment yet"
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Persists <paramref name="configuration"/> as <c>right;left</c> (best-effort).</summary>
    public void Save(GestureConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, $"{configuration.RightBud}{Separator}{configuration.LeftBud}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: if we cannot persist it the choice may not survive a restart,
            // but the app must never fail because of it.
        }
    }

    private static bool TryParseAction(string text, out GestureAction action)
        => Enum.TryParse(text, ignoreCase: true, out action) && Enum.IsDefined(action);

    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PodBridge", "gesture-config.txt");
    }
}
