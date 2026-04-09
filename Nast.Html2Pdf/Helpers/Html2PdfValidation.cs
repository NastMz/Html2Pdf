using System.Text.RegularExpressions;
using Nast.Html2Pdf.Models;
using Nast.Html2Pdf.Services;

namespace Nast.Html2Pdf.Helpers
{
    /// <summary>
    /// Shared validation helpers for Html2Pdf entry points and configuration.
    /// </summary>
    public static partial class Html2PdfValidation
    {
        private static readonly string[] DefaultAllowedSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

        /// <summary>
        /// Ensures a URL uses a supported scheme.
        /// </summary>
        public static Uri EnsureSupportedUri(string value, string parameterName, params string[]? allowedSchemes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("URL cannot be null or empty.", parameterName);
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("URL must be a valid absolute URI.", parameterName);
            }

            var supportedSchemes = allowedSchemes is { Length: > 0 }
                ? allowedSchemes
                : DefaultAllowedSchemes;

            if (!supportedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The URL must use one of the supported URL schemes: {string.Join(", ", supportedSchemes)}.",
                    parameterName);
            }

            return uri;
        }

        /// <summary>
        /// Ensures text input is present and non-whitespace.
        /// </summary>
        public static string EnsureRequiredText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{parameterName} cannot be null or empty.", parameterName);
            }

            return value;
        }

        /// <summary>
        /// Ensures a file path exists before downstream processing.
        /// </summary>
        public static string EnsureExistingFilePath(string value, string parameterName)
        {
            EnsureRequiredText(value, parameterName);

            if (!File.Exists(value))
            {
                throw new FileNotFoundException($"The file provided by {parameterName} does not exist.", value);
            }

            return value;
        }

        /// <summary>
        /// Validates PDF options used during conversion.
        /// </summary>
        public static PdfOptions ValidatePdfOptions(PdfOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            ValidateScale(options.Scale);
            ValidatePositiveTimeout(options.TimeoutMs, "timeoutMs");
            ValidateCustomDimension(options.Width, "width");
            ValidateCustomDimension(options.Height, "height");
            ValidateMargins(options.Margins ?? new PdfMargins());
            ValidateHeaderFooter(options.Header, "header");
            ValidateHeaderFooter(options.Footer, "footer");

            return options;
        }

        /// <summary>
        /// Validates browser pool configuration values.
        /// </summary>
        public static BrowserPoolOptions ValidateBrowserPoolOptions(BrowserPoolOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.MinInstances < 1)
            {
                throw new ArgumentOutOfRangeException("minInstances", options.MinInstances, "minInstances must be greater than zero.");
            }

            if (options.MaxInstances < options.MinInstances)
            {
                throw new ArgumentOutOfRangeException("minInstances", options.MinInstances, "minInstances must be less than or equal to maxInstances.");
            }

            if (options.MaxLifetimeMinutes <= 0)
            {
                throw new ArgumentOutOfRangeException("maxLifetimeMinutes", options.MaxLifetimeMinutes, "maxLifetimeMinutes must be greater than zero.");
            }

            if (options.AcquireTimeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException("acquireTimeoutSeconds", options.AcquireTimeoutSeconds, "acquireTimeoutSeconds must be greater than zero.");
            }

            return options;
        }

        /// <summary>
        /// Validates that a timeout value is positive.
        /// </summary>
        public static int ValidatePositiveTimeout(int timeoutMs, string parameterName)
        {
            if (timeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, timeoutMs, $"{parameterName} must be greater than zero.");
            }

            return timeoutMs;
        }

        private static void ValidateScale(float scale)
        {
            if (scale < 0.1f || scale > 2f)
            {
                throw new ArgumentOutOfRangeException("scale", scale, "scale must be between 0.1 and 2.");
            }
        }

        private static void ValidateCustomDimension(float? value, string parameterName)
        {
            if (value.HasValue && value.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value.Value, $"{parameterName} must be greater than zero.");
            }
        }

        private static void ValidateMargins(PdfMargins margins)
        {
            ValidateMarginValue(margins.Top, "top");
            ValidateMarginValue(margins.Bottom, "bottom");
            ValidateMarginValue(margins.Left, "left");
            ValidateMarginValue(margins.Right, "right");
        }

        private static void ValidateHeaderFooter(PdfHeaderFooter? section, string sectionName)
        {
            if (section == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(section.Template))
            {
                throw new ArgumentException($"{sectionName}.Template cannot be empty.", $"{sectionName}.Template");
            }

            ValidateMarginValue(section.Height, $"{sectionName}.Height");
        }

        private static void ValidateMarginValue(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !MarginRegex().IsMatch(value.Trim()))
            {
                throw new ArgumentException($"{parameterName} must use a supported margin unit (cm, mm, in, px) or 0.", parameterName);
            }
        }

        [GeneratedRegex("^(?:0|(?:\\d+(?:\\.\\d+)?)(?:cm|mm|in|px))$", RegexOptions.IgnoreCase)]
        private static partial Regex MarginRegex();
    }
}
