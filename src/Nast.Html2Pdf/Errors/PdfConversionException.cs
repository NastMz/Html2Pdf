namespace Nast.Html2Pdf;

/// <summary>Stable failure categories of a conversion.</summary>
public enum PdfConversionError
{
    /// <summary>The source, options or output destination is invalid or contradictory. Not retried.</summary>
    InvalidRequest,

    /// <summary>The policy denied an operation: a URL, host, address, local path or script. Not retried.</summary>
    AccessDenied,

    /// <summary>The document, a required sub-resource or a local file could not be loaded.</summary>
    ResourceUnavailable,

    /// <summary>A template could not be parsed or rendered (syntax error, undefined value, unknown filter, limits).</summary>
    TemplateError,

    /// <summary>The queue was full or no rendering slot became available within <see cref="CapacityOptions.QueueTimeout"/>.</summary>
    CapacityExceeded,

    /// <summary>The conversion deadline elapsed.</summary>
    Timeout,

    /// <summary>A configured readiness condition was not satisfied before the deadline.</summary>
    ReadinessNotMet,

    /// <summary>The renderer failed while processing this document (for example the renderer process crashed).</summary>
    RenderingFailed,

    /// <summary>The PDF could not be written to the requested destination; an existing file is left unchanged.</summary>
    OutputFailed,

    /// <summary>The browser could not be started or is not usable (missing executable, unsupported version, launch failure).</summary>
    RendererUnavailable,

    /// <summary>The converter is shutting down or has been disposed.</summary>
    ShuttingDown,
}

/// <summary>
/// Thrown when a conversion fails. <see cref="Error"/> identifies the failure category; the message is safe to log and
/// never contains document content, model data, credentials, query strings or absolute host paths.
/// </summary>
/// <remarks>
/// Cancellation through the caller's <see cref="CancellationToken"/> is reported with <see cref="OperationCanceledException"/>,
/// following .NET conventions; a deadline is reported with <see cref="PdfConversionError.Timeout"/>.
/// </remarks>
public sealed class PdfConversionException : Exception
{
    /// <summary>Creates an exception.</summary>
    public PdfConversionException(PdfConversionError error, string message, string? operationId = null)
        : base(message)
    {
        Error = error;
        OperationId = operationId;
    }

    /// <summary>The failure category.</summary>
    public PdfConversionError Error { get; }

    /// <summary>Identifier of the failed operation, matching log entries and traces; <see langword="null"/> if it failed before starting.</summary>
    public string? OperationId { get; private set; }

    /// <summary>Whether retrying the same request later may succeed (capacity, timeouts, renderer availability, shutdown races).</summary>
    public bool IsTransient => Error is PdfConversionError.CapacityExceeded or PdfConversionError.Timeout
        or PdfConversionError.RendererUnavailable or PdfConversionError.ShuttingDown;

    internal PdfConversionException WithOperationId(string operationId)
    {
        OperationId ??= operationId;
        return this;
    }
}
