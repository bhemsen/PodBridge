using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PodBridge.Windows.Logging;

/// <summary>
/// Minimal, local-only <see cref="ILoggerProvider"/> writing structured log lines to a
/// single rolling, size/age-capped text file under <c>%LOCALAPPDATA%\PodBridge\logs</c>
/// (spec docs/specs/spec-model-coverage-hardening.md: "rolling local file sink capped at
/// ~10 MB / 7 days"). Deliberately hand-rolled instead of adding a third-party file-sink
/// package (Serilog/NLog etc.): it implements only <c>ILoggerProvider</c>/<c>ILogger</c>
/// from the already-referenced <c>Microsoft.Extensions.Logging.Abstractions</c> package, so
/// no new logging framework enters the tree.
/// <para>
/// <b>Local-only (constitution):</b> this type never opens a socket, never references any
/// <c>System.Net.*</c> type, and is the <b>only</b> logging provider registered at the
/// composition root (which calls <c>ILoggingBuilder.ClearProviders()</c> first) — there is
/// no network sink.
/// </para>
/// <para>
/// <see cref="MinLevel"/> is mutable so the tray's "Debug logging" toggle can raise
/// verbosity to the local file only, at runtime, with no restart (Information is the
/// default; Debug is opt-in).
/// </para>
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    /// <summary>Default size cap before the active file rolls (~10 MB).</summary>
    public const long DefaultMaxBytes = 10L * 1024 * 1024;

    /// <summary>Default retention for rolled (historical) files.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);

    private const string ActiveFileName = "podbridge.log";
    private const string RolledFilePrefix = "podbridge-";
    private const string RolledFileSearchPattern = "podbridge-*.log";

    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly TimeSpan _maxAge;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>
    /// Creates the provider and its log directory (existing files are left as-is; ageing
    /// rolled files are purged once at construction and again after every roll).
    /// </summary>
    public RollingFileLoggerProvider(
        string? directory = null,
        long maxBytes = DefaultMaxBytes,
        TimeSpan? maxAge = null,
        LogLevel minLevel = LogLevel.Information,
        TimeProvider? timeProvider = null)
    {
        _directory = directory ?? DefaultDirectory();
        _maxBytes = maxBytes;
        _maxAge = maxAge ?? DefaultMaxAge;
        MinLevel = minLevel;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Directory.CreateDirectory(_directory);
        lock (_gate)
        {
            PurgeAgedRolledFilesLocked();
        }
    }

    /// <summary>
    /// The minimum level written to the file; Information by default. The tray "Debug
    /// logging" toggle sets this to <see cref="LogLevel.Debug"/> and back — the local file
    /// sink only, never a restart, never a network effect.
    /// </summary>
    public LogLevel MinLevel { get; set; }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    // Called by RollingFileLogger for every line that passes MinLevel. Appends, flushes
    // (a diagnostics log must survive a crash), and rolls when the active file has grown
    // past _maxBytes.
    internal void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var writer = EnsureWriterLocked();
            writer.WriteLine(line);
            writer.Flush();
            if (new FileInfo(ActiveFilePath).Length >= _maxBytes)
            {
                RollLocked();
            }
        }
    }

    private string ActiveFilePath => Path.Combine(_directory, ActiveFileName);

    private StreamWriter EnsureWriterLocked()
    {
        if (_writer is not null)
        {
            return _writer;
        }

        // FileShare.ReadWrite (not just Read) so a concurrent reader that itself opens with
        // FileShare.Read (e.g. File.ReadAllText, Notepad, "tail") can succeed while this
        // writer handle stays open — Windows share-mode compatibility is checked in both
        // directions.
        var stream = new FileStream(ActiveFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = false };
        return _writer;
    }

    // Closes the active file, renames it to a timestamped rolled name, purges rolled files
    // older than _maxAge, then opens a fresh empty active file on the next Write.
    private void RollLocked()
    {
        _writer?.Dispose();
        _writer = null;

        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var rolledPath = Path.Combine(_directory, $"{RolledFilePrefix}{timestamp}.log");
        File.Move(ActiveFilePath, rolledPath, overwrite: true);
        PurgeAgedRolledFilesLocked();
    }

    private void PurgeAgedRolledFilesLocked()
    {
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime - _maxAge;
        foreach (var file in Directory.EnumerateFiles(_directory, RolledFileSearchPattern))
        {
            if (File.GetLastWriteTimeUtc(file) >= cutoffUtc)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort retention: a locked/aged file is skipped this cycle, not fatal.
            }
        }
    }

    private static string DefaultDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "PodBridge", "logs");
    }
}
