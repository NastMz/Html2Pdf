namespace Nast.Html2Pdf;

/// <summary>Outcome information shared by every successful conversion.</summary>
public class PdfConversionResult
{
    internal PdfConversionResult(string operationId, long byteCount, ConversionTimings timings, IReadOnlyList<ConversionWarning> warnings)
    {
        OperationId = operationId;
        ByteCount = byteCount;
        Timings = timings;
        Warnings = warnings;
    }

    /// <summary>Identifier of the operation, matching log entries, metrics and traces.</summary>
    public string OperationId { get; }

    /// <summary>Size of the PDF in bytes.</summary>
    public long ByteCount { get; }

    /// <summary>Time spent in each stage.</summary>
    public ConversionTimings Timings { get; }

    /// <summary>Non-fatal issues, for example resources skipped under <see cref="ResourceFailurePolicy.BestEffort"/>.</summary>
    public IReadOnlyList<ConversionWarning> Warnings { get; }
}

/// <summary>A PDF produced in memory.</summary>
public sealed class PdfDocument : PdfConversionResult
{
    private readonly byte[] _content;

    internal PdfDocument(byte[] content, string operationId, ConversionTimings timings, IReadOnlyList<ConversionWarning> warnings)
        : base(operationId, content.Length, timings, warnings)
    {
        _content = content;
    }

    /// <summary>The PDF bytes.</summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>Returns a copy of the PDF bytes.</summary>
    public byte[] ToArray() => (byte[])_content.Clone();

    /// <summary>Opens a read-only stream over the PDF bytes.</summary>
    public Stream OpenRead() => new MemoryStream(_content, writable: false);
}

/// <summary>A PDF that was written and committed to a file.</summary>
public sealed class PdfFileResult : PdfConversionResult
{
    internal PdfFileResult(string filePath, PdfConversionResult result)
        : base(result.OperationId, result.ByteCount, result.Timings, result.Warnings)
    {
        FilePath = filePath;
    }

    /// <summary>Full path of the committed file.</summary>
    public string FilePath { get; }
}

/// <summary>Durations of the conversion stages.</summary>
/// <param name="Queued">Time waiting for a rendering slot.</param>
/// <param name="Preparation">Time preparing input (template rendering, file checks).</param>
/// <param name="Rendering">Time in the browser, including retries.</param>
/// <param name="Output">Time writing the result to its destination.</param>
/// <param name="Total">Total elapsed time.</param>
public sealed record ConversionTimings(TimeSpan Queued, TimeSpan Preparation, TimeSpan Rendering, TimeSpan Output, TimeSpan Total);

/// <summary>Codes for <see cref="ConversionWarning"/>.</summary>
public enum ConversionWarningCode
{
    /// <summary>A sub-resource could not be loaded and was skipped.</summary>
    ResourceUnavailable,
    /// <summary>A sub-resource was denied by policy and was skipped.</summary>
    ResourceDenied,
    /// <summary>A renderer infrastructure failure occurred and the conversion was retried.</summary>
    Retried,
}

/// <summary>A non-fatal issue. <see cref="Resource"/> is redacted: no query string, fragment, credentials or absolute host path.</summary>
/// <param name="Code">Warning category.</param>
/// <param name="Message">Description safe for logs.</param>
/// <param name="Resource">Redacted identifier of the resource concerned, if any.</param>
public sealed record ConversionWarning(ConversionWarningCode Code, string Message, string? Resource = null);
