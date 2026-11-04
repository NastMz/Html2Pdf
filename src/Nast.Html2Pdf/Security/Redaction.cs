namespace Nast.Html2Pdf.Security;

/// <summary>Produces identifiers for resources that are safe to place in errors, warnings and logs.</summary>
internal static class Redaction
{
    private const int MaxSegmentLength = 64;

    /// <summary>
    /// Remote URLs become <c>scheme://host[:port]/…/last-segment</c> (no credentials, query or fragment, no intermediate
    /// path); local virtual URLs become the path relative to their root; <c>data:</c> URLs become their media type.
    /// </summary>
    public static string Url(string url)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var end = url.IndexOfAny([';', ',']);
            return end > 5 ? "data:" + Truncate(url[5..end]) : "data:";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "(invalid URL)";
        }

        if (LocalOrigins.TryGetRootIndex(uri, out _))
        {
            return Truncate(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')), 160);
        }

        if (uri.Host.EndsWith(LocalOrigins.DocumentHostSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return "(document)";
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return uri.Scheme + ":";
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = segments.Length switch
        {
            0 => "/",
            1 => "/" + Truncate(segments[0]),
            _ => "/…/" + Truncate(segments[^1]),
        };
        return $"{uri.Scheme}://{uri.IdnHost}{(uri.IsDefaultPort ? string.Empty : ":" + uri.Port)}{path}";
    }

    private static string Truncate(string value, int max = MaxSegmentLength)
        => value.Length <= max ? value : value[..max] + "…";
}
