using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Diagnostics;

/// <summary>Logger decorator that swallows exceptions thrown by logging providers.</summary>
internal sealed class SafeLogger(ILogger inner) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        try
        {
            return inner.BeginScope(state);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        try
        {
            return inner.IsEnabled(logLevel);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Observability must never change the outcome of a conversion.
        }
    }
}
