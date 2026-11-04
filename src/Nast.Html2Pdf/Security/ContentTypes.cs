using System.Text;

namespace Nast.Html2Pdf.Security;

internal static class ContentTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html", [".htm"] = "text/html", [".xhtml"] = "application/xhtml+xml",
        [".css"] = "text/css", [".js"] = "text/javascript", [".mjs"] = "text/javascript",
        [".json"] = "application/json", [".xml"] = "application/xml", [".txt"] = "text/plain",
        [".svg"] = "image/svg+xml", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".avif"] = "image/avif", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon",
        [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".otf"] = "font/otf",
        [".eot"] = "application/vnd.ms-fontobject",
    };

    public static string ForFile(string path, ReadOnlySpan<byte> content)
    {
        var type = ByExtension.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        return type is "text/html" ? HtmlContentType(content) : type is "text/css" or "text/plain" or "text/javascript" ? type + "; charset=utf-8" : type;
    }

    /// <summary>
    /// A declared encoding (byte order mark or <c>charset</c> in the first 1024 bytes) is honoured; undeclared
    /// documents are decoded as UTF-8 instead of a locale-dependent legacy encoding.
    /// </summary>
    public static string HtmlContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) || content.StartsWith(new byte[] { 0xFE, 0xFF }) || content.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return "text/html";
        }

        var head = Encoding.ASCII.GetString(content[..Math.Min(content.Length, 1024)]);
        return head.Contains("charset", StringComparison.OrdinalIgnoreCase) ? "text/html" : "text/html; charset=utf-8";
    }
}
