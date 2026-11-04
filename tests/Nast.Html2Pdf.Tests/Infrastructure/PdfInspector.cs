using Docnet.Core;
using Docnet.Core.Models;
using UglyToad.PdfPig;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>
/// Independent inspection of produced PDFs. Structure, text, geometry, links and fonts are read with PdfPig (strict
/// parsing); pixels are rendered with PDFium through Docnet. Neither shares code with the Chromium/Skia writer.
/// </summary>
internal sealed class PdfInspector : IDisposable
{
    private const double PointsPerMillimeter = 72 / 25.4;
    private readonly byte[] _bytes;
    private readonly PigDocument _document;

    public PdfInspector(ReadOnlyMemory<byte> pdf)
    {
        _bytes = pdf.ToArray();
        _document = PigDocument.Open(_bytes, new ParsingOptions { UseLenientParsing = false });
    }

    public int PageCount => _document.NumberOfPages;

    public Page Page(int number) => _document.GetPage(number);

    public string Text(int page) => string.Join(" ", Page(page).GetWords().Select(w => w.Text));

    public string AllText => string.Join("\n", Enumerable.Range(1, PageCount).Select(Text));

    public (double Width, double Height) PageSizeMm(int page)
    {
        var p = Page(page);
        return (p.Width / PointsPerMillimeter, p.Height / PointsPerMillimeter);
    }

    public IReadOnlyList<string> LinkUris(int page) => Page(page).GetAnnotations()
        .Where(a => a.Type == AnnotationType.Link && a.Action is UglyToad.PdfPig.Actions.UriAction)
        .Select(a => ((UglyToad.PdfPig.Actions.UriAction)a.Action!).Uri)
        .ToList();

    public int ImageCount(int page) => Page(page).GetImages().Count();

    /// <summary>BaseFont names of all fonts used by the pages, including inside form XObjects (embedded subsets carry a "ABCDEF+" prefix).</summary>
    public IReadOnlyCollection<string> FontNames
    {
        get
        {
            var names = new HashSet<string>();
            var visited = new HashSet<DictionaryToken>(ReferenceEqualityComparer.Instance);
            for (var i = 1; i <= PageCount; i++)
            {
                CollectFonts(Page(i).Dictionary, names, visited);
            }

            return names;
        }
    }

    private IToken Resolve(IToken token) => token is IndirectReferenceToken reference ? _document.Structure.GetObject(reference.Data).Data : token;

    private void CollectFonts(DictionaryToken owner, HashSet<string> names, HashSet<DictionaryToken> visited)
    {
        if (!visited.Add(owner) || !owner.Data.TryGetValue("Resources", out var resourcesToken) || Resolve(resourcesToken) is not DictionaryToken resources)
        {
            return;
        }

        if (resources.Data.TryGetValue("Font", out var fontsToken) && Resolve(fontsToken) is DictionaryToken fonts)
        {
            foreach (var font in fonts.Data.Values.Select(Resolve).OfType<DictionaryToken>())
            {
                if (font.Data.TryGetValue("BaseFont", out var baseFont) && Resolve(baseFont) is NameToken name)
                {
                    names.Add(name.Data);
                }
                else if (font.Data.TryGetValue("FontDescriptor", out var descriptorToken) && Resolve(descriptorToken) is DictionaryToken descriptor
                         && descriptor.Data.TryGetValue("FontName", out var fontName) && Resolve(fontName) is NameToken type3Name)
                {
                    names.Add(type3Name.Data); // Type3 fonts carry their name in the descriptor
                }

                CollectFonts(font, names, visited); // Type3 glyph procedures have their own resources
            }
        }

        if (resources.Data.TryGetValue("XObject", out var xobjectsToken) && Resolve(xobjectsToken) is DictionaryToken xobjects)
        {
            foreach (var value in xobjects.Data.Values)
            {
                if (Resolve(value) is StreamToken stream)
                {
                    CollectFonts(stream.StreamDictionary, names, visited);
                }
            }
        }
    }

    public bool HasBookmarks => _document.TryGetBookmarks(out var bookmarks) && bookmarks.GetNodes().Any();

    /// <summary>Location of a word on a page in millimetres from the top-left corner.</summary>
    public (double X, double Y)? FindWordMm(int page, string word)
    {
        var p = Page(page);
        var match = p.GetWords().FirstOrDefault(w => w.Text.Contains(word, StringComparison.Ordinal));
        return match is null ? null : (match.BoundingBox.Left / PointsPerMillimeter, (p.Height - match.BoundingBox.Top) / PointsPerMillimeter);
    }

    /// <summary>Renders a page and returns the colour at a position given in millimetres from the top-left corner.</summary>
    public Raster Render(int page, int dpi = 72)
    {
        var (widthMm, heightMm) = PageSizeMm(page);
        lock (Docnet)
        {
            using var reader = DocLib.Instance.GetDocReader(_bytes, new PageDimensions(dpi / 72.0));
            using var pageReader = reader.GetPageReader(page - 1);
            return new Raster(pageReader.GetImage(), pageReader.GetPageWidth(), pageReader.GetPageHeight(), widthMm, heightMm);
        }
    }

    private static readonly object Docnet = new();

    public void Dispose() => _document.Dispose();
}

internal sealed class Raster(byte[] bgra, int width, int height, double widthMm, double heightMm)
{
    public (byte R, byte G, byte B) At(double xMm, double yMm)
    {
        var x = Math.Clamp((int)(xMm / widthMm * width), 0, width - 1);
        var y = Math.Clamp((int)(yMm / heightMm * height), 0, height - 1);
        var i = (y * width + x) * 4;
        // PDFium renders transparent pixels as zero alpha; treat them as white paper.
        if (bgra[i + 3] == 0)
        {
            return (255, 255, 255);
        }

        return (bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    public static bool IsColor((byte R, byte G, byte B) pixel, string color) => color switch
    {
        "red" => pixel.R > 200 && pixel.G < 80 && pixel.B < 80,
        "green" => pixel.G > 100 && pixel.R < 80 && pixel.B < 80,
        "blue" => pixel.B > 200 && pixel.R < 80 && pixel.G < 80,
        "white" => pixel.R > 240 && pixel.G > 240 && pixel.B > 240,
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    /// <summary>Extent, in millimetres along a vertical line, of the first run of the given colour.</summary>
    public (double Start, double End)? VerticalRun(double xMm, string color, double stepMm = 0.25)
    {
        double? start = null;
        for (var y = 0.0; y < heightMm; y += stepMm)
        {
            var match = IsColor(At(xMm, y), color);
            if (match && start is null)
            {
                start = y;
            }
            else if (!match && start is not null)
            {
                return (start.Value, y);
            }
        }

        return start is null ? null : (start.Value, heightMm);
    }
}
