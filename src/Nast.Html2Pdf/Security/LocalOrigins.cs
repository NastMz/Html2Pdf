using System.Globalization;

namespace Nast.Html2Pdf.Security;

/// <summary>
/// Virtual origins used to serve documents and local files to Chromium. The <c>.invalid</c> top-level domain is
/// reserved and never resolves, and every request to these hosts is answered by the library.
/// </summary>
internal static class LocalOrigins
{
    public const string RootHostPrefix = "r";
    public const string RootHostSuffix = ".files.html2pdf.invalid";
    public const string DocumentHostSuffix = ".doc.html2pdf.invalid";

    public static Uri RootBase(int rootIndex) => new($"http://{RootHostPrefix}{rootIndex.ToString(CultureInfo.InvariantCulture)}{RootHostSuffix}/");

    /// <summary>Origin for a document without a base directory; unique per operation.</summary>
    public static Uri DocumentUrl(string operationId) => new($"http://op-{operationId}{DocumentHostSuffix}/document.html");

    public static bool IsLocal(Uri uri)
        => uri.Host.EndsWith(RootHostSuffix, StringComparison.OrdinalIgnoreCase)
           || uri.Host.EndsWith(DocumentHostSuffix, StringComparison.OrdinalIgnoreCase);

    public static bool TryGetRootIndex(Uri uri, out int index)
    {
        index = -1;
        var host = uri.Host;
        if (!host.EndsWith(RootHostSuffix, StringComparison.OrdinalIgnoreCase) || !host.StartsWith(RootHostPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digits = host[RootHostPrefix.Length..^RootHostSuffix.Length];
        return digits.Length is > 0 and < 4 && digits.All(char.IsAsciiDigit)
            && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
