using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>Captures formatted log messages, optionally throwing to simulate a broken logging pipeline.</summary>
internal sealed class CapturingLoggerProvider(LogLevel minimum = LogLevel.Trace, bool throwOnLog = false) : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (owner._throwOnLog)
            {
                throw new InvalidOperationException("Logging pipeline failure.");
            }

            if (IsEnabled(logLevel))
            {
                owner.Entries.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception)));
            }
        }
    }

    private readonly LogLevel _minimum = minimum;
    private readonly bool _throwOnLog = throwOnLog;
}
