namespace Nast.Html2Pdf;

/// <summary>Per-conversion layout, readiness and failure-handling settings.</summary>
/// <remarks>
/// Security settings (network, local files, scripts) are deliberately not part of this type: they belong to the
/// converter (<see cref="Html2PdfOptions"/>) so that an individual job or document cannot widen them.
/// </remarks>
public sealed class PdfConversionOptions
{
    /// <summary>Paper size. Default: <see cref="PaperSize.A4"/>.</summary>
    public PaperSize PaperSize { get; set; } = PaperSize.A4;

    /// <summary>Orientation. Default: <see cref="PageOrientation.Portrait"/>.</summary>
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;

    /// <summary>Margins between the paper edge and the document body. Default: 10mm on every side.</summary>
    public PageMargins Margins { get; set; } = PageMargins.Default;

    /// <summary>Whether the job's or the document's <c>@page</c> geometry wins. Default: <see cref="PageLayoutPrecedence.Job"/>.</summary>
    public PageLayoutPrecedence LayoutPrecedence { get; set; } = PageLayoutPrecedence.Job;

    /// <summary>Rendering scale between 0.1 and 2. Default: 1.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Whether background colours and images are printed. Default: <see langword="true"/>.</summary>
    public bool PrintBackground { get; set; } = true;

    /// <summary>CSS media type used for layout. Default: <see cref="CssMediaType.Print"/>.</summary>
    public CssMediaType MediaType { get; set; } = CssMediaType.Print;

    /// <summary>
    /// Optional page ranges to include, for example <c>"1-3, 5"</c>. Ranges beyond the document's page count fail
    /// the conversion with <see cref="PdfConversionError.InvalidRequest"/>.
    /// </summary>
    public string? PageRanges { get; set; }

    /// <summary>Whether a document outline (bookmarks) is generated from headings. Default: <see langword="false"/>.</summary>
    public bool GenerateOutline { get; set; }

    /// <summary>Header drawn in a band directly above the body on every page, or <see langword="null"/>.</summary>
    public PdfHeaderFooter? Header { get; set; }

    /// <summary>Footer drawn in a band directly below the body on every page, or <see langword="null"/>.</summary>
    public PdfHeaderFooter? Footer { get; set; }

    /// <summary>
    /// Data for header and footer templates. When <see langword="null"/>, template sources
    /// (<see cref="PdfSource.FromTemplate(string, object?, TemplateSourceOptions?)"/>) use their own model and other sources use no data.
    /// </summary>
    public object? HeaderFooterModel { get; set; }

    /// <summary>Additional conditions that must hold before the page is printed.</summary>
    public PdfReadinessOptions Readiness { get; set; } = new();

    /// <summary>
    /// How sub-resources that fail to load or are blocked by policy are handled. Default: <see cref="ResourceFailurePolicy.Fail"/>.
    /// The main document must always load.
    /// </summary>
    public ResourceFailurePolicy ResourceFailures { get; set; } = ResourceFailurePolicy.Fail;

    /// <summary>
    /// Deadline for the whole operation: waiting for capacity, preparing input, rendering, retries and writing output.
    /// When <see langword="null"/>, <see cref="Html2PdfOptions.DefaultTimeout"/> applies.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>A header or footer rendered from a trusted Liquid template.</summary>
/// <remarks>
/// <para>The template is rendered with the same escaping rules as document templates. Two values are available in
/// addition to the model: <c>{{ pdf.page_number }}</c> and <c>{{ pdf.total_pages }}</c>.</para>
/// <para>The band occupies exactly <see cref="Height"/>, adjacent to the body, inside the corresponding page margin.
/// The margin must be at least as large as the band, and content taller than the band fails the conversion instead
/// of being clipped or overlapping the body. Header/footer content cannot load external resources: embed images as
/// <c>data:</c> URIs. Document styles do not apply; style the template inline or with a <c>&lt;style&gt;</c> element.</para>
/// </remarks>
public sealed record PdfHeaderFooter
{
    /// <summary>Creates a header or footer.</summary>
    /// <param name="template">Trusted Liquid template.</param>
    /// <param name="height">Height of the band reserved for it.</param>
    public PdfHeaderFooter(string template, Length height)
    {
        ArgumentNullException.ThrowIfNull(template);
        Template = template;
        Height = height;
    }

    /// <summary>Trusted Liquid template.</summary>
    public string Template { get; }

    /// <summary>Height of the band.</summary>
    public Length Height { get; }
}

/// <summary>Conditions evaluated after the document's load event and before printing.</summary>
/// <remarks>
/// The converter always waits for the load event, web fonts and images (including lazily loaded ones) and for all
/// mediated requests to finish. If a configured condition is not met before the deadline, the conversion fails with
/// <see cref="PdfConversionError.ReadinessNotMet"/>.
/// </remarks>
public sealed class PdfReadinessOptions
{
    /// <summary>
    /// When set, the network must stay idle (no mediated request in flight) for this long before printing.
    /// Useful for script-driven documents. Default: <see langword="null"/>.
    /// </summary>
    public TimeSpan? NetworkIdleTime { get; set; }

    /// <summary>When set, an element matching this CSS selector must exist before printing.</summary>
    public string? WaitForSelector { get; set; }

    /// <summary>
    /// When set, this trusted JavaScript expression, supplied by the application, must evaluate to a truthy value before
    /// printing. It is evaluated through the DevTools protocol, independently of whether document scripts are enabled.
    /// </summary>
    public string? WaitForExpression { get; set; }
}

/// <summary>Handling of sub-resources that fail to load or are denied by policy.</summary>
public enum ResourceFailurePolicy
{
    /// <summary>The conversion fails if any required sub-resource is missing or denied.</summary>
    Fail,

    /// <summary>
    /// The conversion succeeds without the resource, and the result carries a <see cref="ConversionWarning"/> for each one.
    /// Denied resources are still never fetched.
    /// </summary>
    BestEffort,
}
