using FluentAssertions;
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
            serviceProvider.GetRequiredService<IHtml2PdfService>().Should().NotBeNull();
            serviceProvider.GetRequiredService<IBrowserPool>().Should().NotBeNull();
            serviceProvider.GetRequiredService<IPdfConverter>().Should().NotBeNull();
            serviceProvider.GetRequiredService<IHtmlGenerator>().Should().NotBeNull();
        }

        [Fact]
        public void BrowserPoolOptions_WithPuppeteerSharp_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var options = new BrowserPoolOptions();

            // Assert
            options.Should().NotBeNull();
            options.MinInstances.Should().Be(1);
            options.MaxInstances.Should().Be(5);
            options.MaxLifetimeMinutes.Should().Be(60);
            options.AcquireTimeoutSeconds.Should().Be(30);
            options.AdditionalArgs.Should().NotBeNull();
            options.AdditionalArgs.Should().BeEmpty();
            options.Headless.Should().BeTrue();
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
            browserPool.Should().NotBeNull();
            pdfConverter.Should().NotBeNull();
        }
    }
}
