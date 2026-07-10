using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PodBridge.Windows.Logging;

/// <summary>
/// <see cref="ILogger"/> for <see cref="RollingFileLoggerProvider"/>: formats one line per
/// log call (UTC timestamp, level, category, message, exception) and hands it to the
/// provider's file writer. No scope support is needed for this minimal sink.
/// </summary>
internal sealed class RollingFileLogger : ILogger
{
    private readonly RollingFileLoggerProvider _provider;
    private readonly string _category;

    public RollingFileLogger(RollingFileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _provider.MinLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var message = formatter(state, exception);
        var line = $"{timestamp} [{logLevel}] {_category}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        _provider.Write(line);
    }
}
