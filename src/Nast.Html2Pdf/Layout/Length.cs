using System.Globalization;
using System.Text.RegularExpressions;

namespace Nast.Html2Pdf;

/// <summary>Unit of a <see cref="Length"/>.</summary>
public enum LengthUnit
{
    /// <summary>Millimetres.</summary>
    Millimeter,
    /// <summary>Centimetres.</summary>
    Centimeter,
    /// <summary>Inches.</summary>
    Inch,
    /// <summary>Typographic points (1/72 inch).</summary>
    Point,
    /// <summary>CSS pixels (1/96 inch).</summary>
    Pixel,
}

/// <summary>
/// A physical length used for paper sizes, margins and header/footer heights.
/// Values are finite and non-negative; conversion between units is exact up to floating point precision.
/// </summary>
public readonly partial record struct Length
{
    private const double MillimetersPerInch = 25.4;

    /// <summary>Creates a length.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, NaN or infinite.</exception>
    public Length(double value, LengthUnit unit)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A length must be a finite, non-negative number.");
        }

        if (!Enum.IsDefined(unit))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown length unit.");
        }

        Value = value;
        Unit = unit;
    }

    /// <summary>The numeric value expressed in <see cref="Unit"/>.</summary>
    public double Value { get; }

    /// <summary>The unit of <see cref="Value"/>.</summary>
    public LengthUnit Unit { get; }

    /// <summary>A zero length.</summary>
    public static Length Zero => new(0, LengthUnit.Millimeter);

    /// <summary>Creates a length in millimetres.</summary>
    public static Length Millimeters(double value) => new(value, LengthUnit.Millimeter);

    /// <summary>Creates a length in centimetres.</summary>
    public static Length Centimeters(double value) => new(value, LengthUnit.Centimeter);

    /// <summary>Creates a length in inches.</summary>
    public static Length Inches(double value) => new(value, LengthUnit.Inch);

    /// <summary>Creates a length in points (1/72 inch).</summary>
    public static Length Points(double value) => new(value, LengthUnit.Point);

    /// <summary>Creates a length in CSS pixels (1/96 inch).</summary>
    public static Length Pixels(double value) => new(value, LengthUnit.Pixel);

    /// <summary>The length expressed in millimetres.</summary>
    public double ToMillimeters() => Unit switch
    {
        LengthUnit.Millimeter => Value,
        LengthUnit.Centimeter => Value * 10,
        LengthUnit.Inch => Value * MillimetersPerInch,
        LengthUnit.Point => Value * MillimetersPerInch / 72,
        LengthUnit.Pixel => Value * MillimetersPerInch / 96,
        _ => throw new InvalidOperationException("Unknown unit."),
    };

    /// <summary>The length expressed in CSS pixels (1/96 inch).</summary>
    public double ToPixels() => ToMillimeters() / MillimetersPerInch * 96;

    /// <summary>Adds two lengths; the result is expressed in millimetres.</summary>
    public static Length operator +(Length left, Length right) => Millimeters(left.ToMillimeters() + right.ToMillimeters());

    /// <summary>
    /// Parses a length such as <c>"10mm"</c>, <c>"1.5cm"</c>, <c>"0.5in"</c>, <c>"12pt"</c>, <c>"96px"</c> or <c>"0"</c>.
    /// The invariant culture is used.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid length.</exception>
    public static Length Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var length)
            ? length
            : throw new FormatException("A length must be a non-negative number followed by mm, cm, in, pt or px (for example \"10mm\"), or \"0\".");
    }

    /// <summary>Attempts to parse a length; see <see cref="Parse"/>.</summary>
    public static bool TryParse(string? text, out Length length)
    {
        length = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = LengthPattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!double.TryParse(match.Groups["value"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || double.IsInfinity(value))
        {
            return false;
        }

        var unitText = match.Groups["unit"].Value.ToLowerInvariant();
        if (unitText.Length == 0)
        {
            if (value != 0)
            {
                return false;
            }

            length = Zero;
            return true;
        }

        var unit = unitText switch
        {
            "mm" => LengthUnit.Millimeter,
            "cm" => LengthUnit.Centimeter,
            "in" => LengthUnit.Inch,
            "pt" => LengthUnit.Point,
            _ => LengthUnit.Pixel,
        };
        length = new Length(value, unit);
        return true;
    }

    /// <summary>Formats the length as CSS text, for example <c>10mm</c>.</summary>
    public override string ToString() => ToCss();

    internal string ToCss() => Value.ToString("0.####", CultureInfo.InvariantCulture) + Unit switch
    {
        LengthUnit.Millimeter => "mm",
        LengthUnit.Centimeter => "cm",
        LengthUnit.Inch => "in",
        LengthUnit.Point => "pt",
        _ => "px",
    };

    /// <summary>Millimetres formatted for CSS, independent of the declared unit.</summary>
    internal string ToCssMillimeters() => ToMillimeters().ToString("0.####", CultureInfo.InvariantCulture) + "mm";

    /// <summary>Implicitly parses a length from text such as <c>"10mm"</c>.</summary>
    public static implicit operator Length(string text) => Parse(text);

    [GeneratedRegex(@"^(?<value>\d+(\.\d+)?|\.\d+)\s*(?<unit>mm|cm|in|pt|px)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthPattern();
}
