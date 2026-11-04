namespace Nast.Html2Pdf;

/// <summary>
/// What to convert. Create instances with the static factory methods; each kind has a distinct origin and trust level.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="FromHtml"/>: markup supplied by the application. Relative resources resolve against an optional
/// authorized base directory.</item>
/// <item><see cref="FromTemplate(string, object?, TemplateSourceOptions?)"/> and <see cref="FromTemplateFile"/>: a trusted
/// Liquid template rendered with an untrusted model. Model values are HTML-encoded and never interpreted as template code.</item>
/// <item><see cref="FromFile"/>: an HTML file under <see cref="LocalFileOptions.AllowedRoots"/>; relative resources resolve
/// against the file's directory.</item>
/// <item><see cref="FromUrl(Uri)"/>: a remote document, permitted only by <see cref="NetworkOptions.AllowUrlSources"/> and
/// <see cref="NetworkOptions.AllowedHosts"/>. Remote documents never get local file access.</item>
/// </list>
/// </remarks>
public abstract class PdfSource
{
    private protected PdfSource()
    {
    }

    internal abstract string Kind { get; }

    /// <summary>Converts HTML markup.</summary>
    /// <param name="html">The document markup.</param>
    /// <param name="baseDirectory">
    /// Optional absolute directory under <see cref="LocalFileOptions.AllowedRoots"/> against which relative resource
    /// references resolve. Without it, the document has no local file access.
    /// </param>
    public static PdfSource FromHtml(string html, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new HtmlSource(html, baseDirectory);
    }

    /// <summary>Renders a trusted Liquid template with an untrusted model and converts the result.</summary>
    /// <param name="template">The trusted template text.</param>
    /// <param name="model">Data available to the template. Its values are HTML-encoded unless they are <see cref="TrustedHtml"/>.</param>
    /// <param name="options">Partials and base directory.</param>
    public static PdfSource FromTemplate(string template, object? model, TemplateSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new TemplateSource(template, null, model, options ?? new TemplateSourceOptions());
    }

    /// <summary>
    /// Renders a trusted Liquid template file located under <see cref="LocalFileOptions.AllowedRoots"/>. Relative resources
    /// resolve against the template's directory unless <see cref="TemplateSourceOptions.BaseDirectory"/> is set.
    /// </summary>
    public static PdfSource FromTemplateFile(string templatePath, object? model, TemplateSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(templatePath);
        return new TemplateSource(null, templatePath, model, options ?? new TemplateSourceOptions());
    }

    /// <summary>Converts an HTML file located under <see cref="LocalFileOptions.AllowedRoots"/>.</summary>
    /// <param name="path">Absolute path of the file.</param>
    public static PdfSource FromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new FileSource(path);
    }

    /// <summary>Converts a remote document. Requires <see cref="NetworkOptions.AllowUrlSources"/> and an allowed host.</summary>
    public static PdfSource FromUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return new UrlSource(url);
    }

    /// <summary>Converts a remote document; see <see cref="FromUrl(Uri)"/>.</summary>
    /// <exception cref="ArgumentException">The text is not an absolute URI.</exception>
    public static PdfSource FromUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new UrlSource(uri)
            : throw new ArgumentException("The URL must be absolute.", nameof(url));
    }
}

/// <summary>Options for template sources.</summary>
public sealed class TemplateSourceOptions
{
    /// <summary>
    /// Trusted partial templates by name, usable with <c>{% render 'name' %}</c> or <c>{% include 'name' %}</c>.
    /// Only these names can be resolved; neither the model nor the document can supply partials.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Partials { get; init; }

    /// <summary>
    /// Absolute directory under <see cref="LocalFileOptions.AllowedRoots"/> against which relative resources resolve.
    /// For template files this defaults to the template's directory; for in-memory templates there is no default and the
    /// document has no local file access.
    /// </summary>
    public string? BaseDirectory { get; init; }
}

internal sealed class HtmlSource(string html, string? baseDirectory) : PdfSource
{
    public string Html { get; } = html;
    public string? BaseDirectory { get; } = baseDirectory;
    internal override string Kind => "html";
}

internal sealed class TemplateSource(string? template, string? templatePath, object? model, TemplateSourceOptions options) : PdfSource
{
    public string? Template { get; } = template;
    public string? TemplatePath { get; } = templatePath;
    public object? Model { get; } = model;
    public TemplateSourceOptions Options { get; } = options;
    internal override string Kind => TemplatePath is null ? "template" : "template-file";
}

internal sealed class FileSource(string path) : PdfSource
{
    public string Path { get; } = path;
    internal override string Kind => "file";
}

internal sealed class UrlSource(Uri url) : PdfSource
{
    public Uri Url { get; } = url;
    internal override string Kind => "url";
}
