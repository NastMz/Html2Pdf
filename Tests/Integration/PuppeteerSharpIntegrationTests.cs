using Shouldly;
using Moq;
using Nast.Html2Pdf.Abstractions;
using Nast.Html2Pdf.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Extensions;

namespace Nast.Html2Pdf.Tests.Integration
{
    public class PuppeteerSharpIntegrationTests
    {
        [Fact]
        public void ServiceRegistration_WithPuppeteerSharp_ShouldRegisterAllServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole());
            services.AddHtml2Pdf();

            var serviceProvider = services.BuildServiceProvider();

            // Act & Assert - Just validate that services are registered
            serviceProvider.GetRequiredService<IHtml2PdfService>().ShouldNotBeNull();
            serviceProvider.GetRequiredService<IBrowserPool>().ShouldNotBeNull();
            serviceProvider.GetRequiredService<IPdfConverter>().ShouldNotBeNull();
            serviceProvider.GetRequiredService<IHtmlGenerator>().ShouldNotBeNull();
        }

        [Fact]
        public void BrowserPoolOptions_WithPuppeteerSharp_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var options = new BrowserPoolOptions();

            // Assert
            options.ShouldNotBeNull();
            options.MinInstances.ShouldBe(1);
            options.MaxInstances.ShouldBe(5);
            options.MaxLifetimeMinutes.ShouldBe(60);
            options.AcquireTimeoutSeconds.ShouldBe(30);
            options.AdditionalArgs.ShouldNotBeNull();
            options.AdditionalArgs.ShouldBeEmpty();
            options.Headless.ShouldBeTrue();
        }

        [Fact]
        public void PuppeteerSharpMigration_CompileCheck_ShouldCompileSuccessfully()
        {
            // This test verifies that the migration to PuppeteerSharp compiled successfully
            // by simply instantiating the main services without browser execution

            // Arrange
            var logger = new Mock<ILogger<BrowserPool>>();
            var options = new BrowserPoolOptions();

            // Act - Just create instances to verify compilation
            var browserPool = new BrowserPool(options, logger.Object);
            var pdfConverterLogger = new Mock<ILogger<PdfConverter>>();
            var pdfConverter = new PdfConverter(browserPool, pdfConverterLogger.Object);

            // Assert
            browserPool.ShouldNotBeNull();
            pdfConverter.ShouldNotBeNull();
        }
    }
}
