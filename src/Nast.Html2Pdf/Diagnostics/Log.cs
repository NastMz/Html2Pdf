using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Diagnostics;

/// <summary>
/// Log messages. They contain operation identifiers, categories, durations and redacted resource identifiers only:
/// never document content, model data, query strings or absolute paths. Raw exception details are logged only at
/// <see cref="LogLevel.Trace"/>, which operators must treat as potentially sensitive.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Debug, "Conversion {OperationId} admitted ({Source}) after {QueueMs} ms in queue")]
    public static partial void Admitted(ILogger logger, string operationId, string source, long queueMs);

    [LoggerMessage(2, LogLevel.Information, "Conversion {OperationId} ({Source}) succeeded in {TotalMs} ms: {Bytes} bytes, {Warnings} warning(s)")]
    public static partial void Succeeded(ILogger logger, string operationId, string source, long totalMs, long bytes, int warnings);

    [LoggerMessage(3, LogLevel.Warning, "Conversion {OperationId} ({Source}) failed after {TotalMs} ms: {Error}. {Reason}")]
    public static partial void Failed(ILogger logger, string operationId, string source, long totalMs, PdfConversionError error, string reason);

    [LoggerMessage(4, LogLevel.Information, "Conversion {OperationId} ({Source}) was cancelled by the caller after {TotalMs} ms")]
    public static partial void Cancelled(ILogger logger, string operationId, string source, long totalMs);

    [LoggerMessage(5, LogLevel.Warning, "Conversion {OperationId}: renderer infrastructure failure ({ExceptionType}) on attempt {Attempt}; retrying")]
    public static partial void Retrying(ILogger logger, string operationId, string exceptionType, int attempt);

    [LoggerMessage(6, LogLevel.Debug, "Conversion {OperationId}: {Disposition} {Resource} ({Reason})")]
    public static partial void ResourceIssue(ILogger logger, string operationId, string disposition, string resource, string reason);

    [LoggerMessage(7, LogLevel.Information, "Browser generation {Generation} started: {Version}, process {ProcessId}")]
    public static partial void BrowserStarted(ILogger logger, int generation, string version, int processId);

    [LoggerMessage(8, LogLevel.Error, "Browser failed to start ({ExceptionType})")]
    public static partial void BrowserLaunchFailed(ILogger logger, string exceptionType);

    [LoggerMessage(9, LogLevel.Trace, "Browser launch failure detail")]
    public static partial void BrowserLaunchFailureDetail(ILogger logger, Exception exception);

    [LoggerMessage(10, LogLevel.Information, "Browser generation {Generation} retired: {Reason}")]
    public static partial void BrowserRetired(ILogger logger, int generation, string reason);

    [LoggerMessage(11, LogLevel.Warning, "Browser generation {Generation} did not close cleanly ({ExceptionType}); process terminated")]
    public static partial void BrowserCloseFailed(ILogger logger, int generation, string exceptionType);

    [LoggerMessage(12, LogLevel.Trace, "Conversion {OperationId} internal failure detail")]
    public static partial void FailureDetail(ILogger logger, string operationId, Exception exception);

    [LoggerMessage(13, LogLevel.Information, "Converter shutting down: {Active} active and {Queued} queued conversion(s)")]
    public static partial void ShuttingDown(ILogger logger, int active, int queued);

    [LoggerMessage(14, LogLevel.Warning, "Converter shutdown timeout elapsed; cancelling {Active} active conversion(s)")]
    public static partial void ShutdownCancelling(ILogger logger, int active);
}
