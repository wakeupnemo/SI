using Avalonia.Logging;
using NLog;
using System.Globalization;

namespace SIQuester.Desktop;

/// <summary>Persists important Avalonia runtime diagnostics through the bounded application log.</summary>
internal sealed class AvaloniaPersistentLogSink : ILogSink
{
    private readonly NLog.Logger _logger = LogManager.GetLogger("Avalonia");

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Error;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Write(level, area, source, messageTemplate);

    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        params object?[] propertyValues) =>
        Write(level, area, source, FormatMessage(messageTemplate, propertyValues));

    private void Write(LogEventLevel level, string area, object? source, string message)
    {
        var sourceName = source?.GetType().Name ?? "none";
        _logger.Log(MapLevel(level), "Avalonia {Area} ({Source}): {Message}", area, sourceName, message);
    }

    private static string FormatMessage(string template, object?[] values)
    {
        if (values.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, values);
        }
        catch (FormatException)
        {
            return $"{template} [{string.Join(", ", values.Select(value => value?.ToString() ?? "null"))}]";
        }
    }

    private static NLog.LogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Fatal => NLog.LogLevel.Fatal,
        LogEventLevel.Error => NLog.LogLevel.Error,
        LogEventLevel.Warning => NLog.LogLevel.Warn,
        LogEventLevel.Information => NLog.LogLevel.Info,
        LogEventLevel.Debug => NLog.LogLevel.Debug,
        _ => NLog.LogLevel.Trace,
    };
}
