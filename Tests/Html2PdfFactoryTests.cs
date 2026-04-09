namespace Nast.Html2Pdf.Tests
{
    public class Html2PdfFactoryTests
    {
        [Fact]
        public void Create_ShouldCopyAllProvisioningAndHardeningOptions()
        {
            var configuredOptions = new BrowserPoolOptions
            {
                MinInstances = 2,
                MaxInstances = 4,
                MaxLifetimeMinutes = 90,
                AcquireTimeoutSeconds = 45,
                AdditionalArgs = ["--custom-flag"],
                Headless = false,
                ExecutablePath = @"C:\\tools\\chrome.exe",
                BrowserCacheDirectory = @"C:\\browser-cache",
                SkipBrowserDownload = true,
                AllowInsecureCertificates = true,
                DisableSandbox = true
            };

            var service = Html2PdfFactory.Create(configuredOptions);

            var actualOptions = GetBrowserPoolOptions(service);

            actualOptions.MinInstances.ShouldBe(2);
            actualOptions.MaxInstances.ShouldBe(4);
            actualOptions.MaxLifetimeMinutes.ShouldBe(90);
            actualOptions.AcquireTimeoutSeconds.ShouldBe(45);
            actualOptions.AdditionalArgs.ShouldBe(["--custom-flag"]);
            actualOptions.Headless.ShouldBeFalse();
            actualOptions.ExecutablePath.ShouldBe(@"C:\\tools\\chrome.exe");
            actualOptions.BrowserCacheDirectory.ShouldBe(@"C:\\browser-cache");
            actualOptions.SkipBrowserDownload.ShouldBeTrue();
            actualOptions.AllowInsecureCertificates.ShouldBeTrue();
            actualOptions.DisableSandbox.ShouldBeTrue();
        }

        [Fact]
        public void Create_ShouldCloneAdditionalArgsWhenCopyingOptions()
        {
            var configuredOptions = new BrowserPoolOptions
            {
                AdditionalArgs = ["--custom-flag"]
            };

            var service = Html2PdfFactory.Create(configuredOptions);

            var actualOptions = GetBrowserPoolOptions(service);

            actualOptions.AdditionalArgs.ShouldNotBeSameAs(configuredOptions.AdditionalArgs);
            actualOptions.AdditionalArgs.ShouldBe(["--custom-flag"]);
        }

        [Fact]
        public void CreateDefault_ShouldUseProductionSafeDefaults()
        {
            var service = Html2PdfFactory.CreateDefault();

            var actualOptions = GetBrowserPoolOptions(service);

            actualOptions.MinInstances.ShouldBe(1);
            actualOptions.MaxInstances.ShouldBe(3);
            actualOptions.MaxLifetimeMinutes.ShouldBe(30);
            actualOptions.AcquireTimeoutSeconds.ShouldBe(30);
            actualOptions.Headless.ShouldBeTrue();
            actualOptions.AllowInsecureCertificates.ShouldBeFalse();
            actualOptions.DisableSandbox.ShouldBeFalse();
            actualOptions.AdditionalArgs.ShouldBeEmpty();
        }

        private static BrowserPoolOptions GetBrowserPoolOptions(IHtml2PdfService service)
        {
            var pdfConverterField = typeof(Html2PdfService).GetField("_pdfConverter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            pdfConverterField.ShouldNotBeNull();

            var pdfConverter = pdfConverterField!.GetValue(service);
            pdfConverter.ShouldNotBeNull();

            var browserPoolField = typeof(PdfConverter).GetField("_browserPool", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            browserPoolField.ShouldNotBeNull();

            var browserPool = browserPoolField!.GetValue(pdfConverter);
            browserPool.ShouldNotBeNull();

            var optionsField = typeof(BrowserPool).GetField("_options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            optionsField.ShouldNotBeNull();

            var options = optionsField!.GetValue(browserPool);
            options.ShouldBeOfType<BrowserPoolOptions>();

            return (BrowserPoolOptions)options;
        }
    }
}
