namespace Nast.Html2Pdf;

/// <summary>Converts HTML documents to PDF. Implemented by <see cref="Html2PdfConverter"/>; thread-safe.</summary>
/// <remarks>
/// Every method either returns a successful result or throws: <see cref="PdfConversionException"/> for conversion
/// failures (see <see cref="PdfConversionException.Error"/>), <see cref="OperationCanceledException"/> when the caller's
/// token is cancelled, and <see cref="ArgumentNullException"/> for missing arguments.
/// </remarks>
public interface IHtml2PdfConverter
{
    /// <summary>Converts a source to a PDF in memory.</summary>
    Task<PdfDocument> ConvertAsync(PdfSource source, PdfConversionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a source and commits the PDF to <paramref name="outputPath"/> atomically: the file either keeps its previous
    /// content or receives the complete new PDF, never a partial one.
    /// </summary>
    /// <param name="source">What to convert.</param>
    /// <param name="outputPath">Destination file. Its directory must exist.</param>
    /// <param name="overwrite">Whether an existing file may be replaced. Default: <see langword="false"/>.</param>
    /// <param name="options">Conversion options.</param>
    /// <param name="cancellationToken">Observed until the commit point; once the file is being committed the operation completes.</param>
    Task<PdfFileResult> ConvertToFileAsync(PdfSource source, string outputPath, bool overwrite = false, PdfConversionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a source and writes the PDF to <paramref name="destination"/>. Nothing is written unless rendering
    /// succeeds; if writing to the stream fails, the stream may contain partial data.
    /// </summary>
    Task<PdfConversionResult> ConvertToStreamAsync(PdfSource source, Stream destination, PdfConversionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a snapshot of the converter's state.</summary>
    Html2PdfHealth GetHealth();
}
