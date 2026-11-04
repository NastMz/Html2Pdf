namespace Nast.Html2Pdf;

/// <summary>
/// Paper dimensions in portrait orientation. Use the predefined sizes or <see cref="Custom"/>.
/// Orientation is applied separately through <see cref="PdfConversionOptions.Orientation"/>.
/// </summary>
public sealed record PaperSize
{
    private PaperSize(string name, Length width, Length height)
    {
        Name = name;
        Width = width;
        Height = height;
    }

    /// <summary>Display name of the size (for example <c>A4</c> or <c>Custom</c>).</summary>
    public string Name { get; }

    /// <summary>Width in portrait orientation.</summary>
    public Length Width { get; }

    /// <summary>Height in portrait orientation.</summary>
    public Length Height { get; }

#pragma warning disable CS1591 // Self-describing standard paper sizes.
    public static PaperSize A3 { get; } = new("A3", Length.Millimeters(297), Length.Millimeters(420));
    public static PaperSize A4 { get; } = new("A4", Length.Millimeters(210), Length.Millimeters(297));
    public static PaperSize A5 { get; } = new("A5", Length.Millimeters(148), Length.Millimeters(210));
    public static PaperSize A6 { get; } = new("A6", Length.Millimeters(105), Length.Millimeters(148));
    public static PaperSize Letter { get; } = new("Letter", Length.Inches(8.5), Length.Inches(11));
    public static PaperSize Legal { get; } = new("Legal", Length.Inches(8.5), Length.Inches(14));
    public static PaperSize Tabloid { get; } = new("Tabloid", Length.Inches(11), Length.Inches(17));
#pragma warning restore CS1591

    /// <summary>
    /// Creates a custom paper size. Width and height must each be between 10mm and 5000mm
    /// (Chromium cannot print pages outside that range reliably).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is outside the supported range.</exception>
    public static PaperSize Custom(Length width, Length height)
    {
        EnsureDimension(width, nameof(width));
        EnsureDimension(height, nameof(height));
        return new PaperSize("Custom", width, height);
    }

    /// <summary>Resolves a predefined size by name (case-insensitive), for example <c>"A4"</c> or <c>"Letter"</c>.</summary>
    /// <exception cref="ArgumentException">The name is not a known paper size.</exception>
    public static PaperSize FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant() switch
        {
            "A3" => A3,
            "A4" => A4,
            "A5" => A5,
            "A6" => A6,
            "LETTER" => Letter,
            "LEGAL" => Legal,
            "TABLOID" => Tabloid,
            _ => throw new ArgumentException("Unknown paper size. Supported names: A3, A4, A5, A6, Letter, Legal, Tabloid; use PaperSize.Custom for other sizes.", nameof(name)),
        };
    }

    internal const double MinimumMillimeters = 10;
    internal const double MaximumMillimeters = 5000;

    private static void EnsureDimension(Length value, string parameterName)
    {
        var mm = value.ToMillimeters();
        if (mm < MinimumMillimeters || mm > MaximumMillimeters)
        {
            throw new ArgumentOutOfRangeException(parameterName, value.ToString(), "Paper dimensions must be between 10mm and 5000mm.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Width} x {Height})";
}

/// <summary>Page orientation.</summary>
public enum PageOrientation
{
    /// <summary>Height is the longer side.</summary>
    Portrait,
    /// <summary>Width is the longer side; the paper dimensions are swapped.</summary>
    Landscape,
}

/// <summary>Page margins, measured from the paper edge to the document body.</summary>
public sealed record PageMargins
{
    /// <summary>Creates margins.</summary>
    public PageMargins(Length top, Length right, Length bottom, Length left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    /// <summary>Creates equal margins on all sides.</summary>
    public PageMargins(Length all) : this(all, all, all, all)
    {
    }

    /// <summary>Top margin.</summary>
    public Length Top { get; init; }

    /// <summary>Right margin.</summary>
    public Length Right { get; init; }

    /// <summary>Bottom margin.</summary>
    public Length Bottom { get; init; }

    /// <summary>Left margin.</summary>
    public Length Left { get; init; }

    /// <summary>No margins.</summary>
    public static PageMargins None { get; } = new(Length.Zero);

    /// <summary>The default margins: 10mm on every side.</summary>
    public static PageMargins Default { get; } = new(Length.Millimeters(10));
}

/// <summary>
/// Decides which page geometry wins when a document declares its own CSS <c>@page</c> size or margins.
/// </summary>
public enum PageLayoutPrecedence
{
    /// <summary>
    /// The job's paper size, orientation and margins are authoritative. Document <c>@page</c> size and margin
    /// declarations are overridden (other <c>@page</c> rules, such as page breaks, still apply).
    /// </summary>
    Job,

    /// <summary>
    /// A document's CSS <c>@page</c> size and margins are used when declared; the job values are fallbacks.
    /// Header/footer bands are not allowed with this mode because their space cannot be reserved reliably.
    /// </summary>
    Document,
}

/// <summary>CSS media type used for layout.</summary>
public enum CssMediaType
{
    /// <summary>Print media rules (<c>@media print</c>) apply. This is the normal choice for documents.</summary>
    Print,
    /// <summary>Screen media rules apply, as when the page is displayed in a browser.</summary>
    Screen,
}
