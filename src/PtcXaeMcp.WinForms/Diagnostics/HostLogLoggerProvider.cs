using Microsoft.Extensions.Logging;

namespace PtcXaeMcp.WinForms.Diagnostics;

/// <summary>
/// Adapts <see cref="Microsoft.Extensions.Logging"/> (ASP.NET Core / Kestrel /
/// the MCP SDK) into <see cref="HostLog"/> — replaces the bare
/// <c>builder.Logging.ClearProviders()</c> in <c>McpBridge.cs</c> (issue #147
/// item 3) so a Kestrel/transport warning or error survives in the same host
/// log instead of vanishing. Only <see cref="LogLevel.Warning"/> and above are
/// forwarded; anything below is a no-op (checked via <see cref="IsEnabled"/>
/// so the framework can skip formatting entirely).
/// </summary>
public sealed class HostLogLoggerProvider(HostLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new HostLogLogger(log, categoryName);

    public void Dispose() { /* HostLog is process-shared; nothing owned here. */ }

    private sealed class HostLogLogger(HostLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string message;
            try { message = formatter(state, exception); }
            catch { message = "(unformattable log message)"; }

            var header = $"[{category}] {message}";
            if (exception is not null)
                log.WriteException(header, exception);
            else
                log.Write(logLevel == LogLevel.Critical ? "CRITICAL" : "WARN", header);
        }
    }
}
