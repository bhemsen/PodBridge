using Microsoft.Win32;

namespace PodBridge.Windows.Interop;

// Win32 registry seam for the portable HKCU Run-key auto-start adapter (issue #117).
// RunKeyStartupToggle reaches the registry ONLY through the interface below; the real
// implementation wraps Microsoft.Win32.Registry (pure BCL, single-file-safe, no admin
// required for the per-user HKCU hive) and PodBridge.Windows.Tests substitutes a fake so
// the enable/disable/self-heal/DisabledByUser logic is exercised with no real registry
// write (docs/research/release-1.0.md §4).

/// <summary>
/// Reads and writes the single <c>PodBridge</c> value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, plus the read-only
/// Task-Manager "disabled" flag under the sibling <c>StartupApproved\Run</c> key.
/// </summary>
internal interface IRunKeyRegistry
{
    /// <summary>The stored command line, or <see langword="null"/> when no entry exists.</summary>
    string? GetRunValue();

    /// <summary>Writes (or overwrites) the <c>PodBridge</c> Run value.</summary>
    void SetRunValue(string commandLine);

    /// <summary>Removes the <c>PodBridge</c> Run value, if present.</summary>
    void DeleteRunValue();

    /// <summary>
    /// <see langword="true"/> when the user disabled the entry from Task Manager's Startup
    /// tab — Windows records this as a binary <c>StartupApproved\Run</c> value whose first
    /// byte is <c>0x03</c> (community-reverse-engineered format; docs/research/release-1.0.md §4).
    /// The original Run value is left in place but inert, and this flag must never be
    /// overridden by the app.
    /// </summary>
    bool IsDisabledByUser();
}

/// <summary>
/// Real per-user HKCU implementation of <see cref="IRunKeyRegistry"/>. Needs no admin
/// rights and no COM: <c>Microsoft.Win32.Registry</c> is pure BCL and single-file-safe.
/// </summary>
internal sealed class Win32RunKeyRegistry : IRunKeyRegistry
{
    private const string ValueName = "PodBridge";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public string? GetRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void SetRunValue(string commandLine)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, commandLine, RegistryValueKind.String);
    }

    public void DeleteRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool IsDisabledByUser()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath);
        return key?.GetValue(ValueName) is byte[] { Length: > 0 } flag && flag[0] == 0x03;
    }
}
