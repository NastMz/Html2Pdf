namespace Nast.Html2Pdf;

/// <summary>
/// Markup the application explicitly trusts. Template output of model values is HTML-encoded, except for values of this
/// type, which are inserted verbatim. This is the only way to insert markup through data: the Liquid <c>raw</c> filter
/// is not available.
/// </summary>
/// <remarks>Never wrap content that originates from end users or other untrusted sources.</remarks>
public sealed class TrustedHtml
{
    /// <summary>Wraps trusted markup.</summary>
    public TrustedHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        Html = html;
    }

    /// <summary>The trusted markup.</summary>
    public string Html { get; }

    /// <inheritdoc />
    public override string ToString() => Html;
}
