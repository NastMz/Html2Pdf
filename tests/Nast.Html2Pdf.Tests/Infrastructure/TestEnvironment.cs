using Microsoft.Extensions.Logging;

namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>Settings for tests that use a real browser.</summary>
internal static class TestEnvironment
{
    /// <summary>
    /// Chromium refuses to run its sandbox as root, and some CI kernels block the user namespaces it needs. Tests
    /// disable it only in those cases, mirroring the documented deployment guidance.
    /// </summary>
    public static bool DisableSandbox =>
        string.Equals(Environment.GetEnvironmentVariable("HTML2PDF_TEST_DISABLE_SANDBOX"), "true", StringComparison.OrdinalIgnoreCase)
        || (OperatingSystem.IsLinux() && Environment.UserName == "root");

    public static Html2PdfOptions Options(Action<Html2PdfOptions>? configure = null)
    {
        var options = new Html2PdfOptions
        {
            Browser =
            {
                DisableSandbox = DisableSandbox,
                TemporaryDirectory = TempDirectory.CreateRoot("profiles"),
                // Cold Chrome starts on loaded CI machines (notably Windows with real-time scanning) can exceed the
                // 30 s default. Launch-timeout behaviour itself is covered by a dedicated test.
                LaunchTimeout = TimeSpan.FromSeconds(90),
            },
            DefaultTimeout = TimeSpan.FromSeconds(45),
        };
        configure?.Invoke(options);
        return options;
    }

    public static Html2PdfConverter Create(Action<Html2PdfOptions>? configure = null, ILoggerFactory? loggerFactory = null)
        => new(Options(configure), loggerFactory);
}
