namespace Nast.Html2Pdf.Rendering;

/// <summary>Finds the Chromium executable. The library never downloads a browser.</summary>
internal static class ChromiumLocator
{
    public static string Locate(BrowserOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            return File.Exists(options.ExecutablePath)
                ? options.ExecutablePath
                : throw new PdfConversionException(PdfConversionError.RendererUnavailable,
                    "The configured Chromium executable (Html2PdfOptions.Browser.ExecutablePath) does not exist.");
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(BrowserOptions.ExecutablePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return File.Exists(fromEnvironment)
                ? fromEnvironment
                : throw new PdfConversionException(PdfConversionError.RendererUnavailable,
                    $"The Chromium executable named by {BrowserOptions.ExecutablePathEnvironmentVariable} does not exist.");
        }

        if (options.SearchStandardLocations)
        {
            foreach (var candidate in StandardLocations())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new PdfConversionException(PdfConversionError.RendererUnavailable,
            "No Chromium executable was found. Install Chrome or Chromium and set Html2PdfOptions.Browser.ExecutablePath " +
            $"or the {BrowserOptions.ExecutablePathEnvironmentVariable} environment variable. The library does not download browsers.");
    }

    internal static IEnumerable<string> StandardLocations()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     }.Where(r => !string.IsNullOrEmpty(r)))
            {
                yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(root, "Chromium", "Application", "chrome.exe");
                yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            yield return "/Applications/Chromium.app/Contents/MacOS/Chromium";
            yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
        }
        else
        {
            yield return "/usr/bin/chromium";
            yield return "/usr/bin/chromium-browser";
            yield return "/usr/bin/google-chrome";
            yield return "/usr/bin/google-chrome-stable";
            yield return "/usr/bin/chrome-headless-shell";
            yield return "/usr/bin/microsoft-edge";
        }
    }
}
