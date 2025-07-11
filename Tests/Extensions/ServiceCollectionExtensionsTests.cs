using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nast.Html2Pdf.Models;

namespace Nast.Html2Pdf.Tests.Extensions
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddHtml2Pdf_ShouldRegisterAllServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddHtml2Pdf();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            serviceProvider.GetService<IBrowserPool>().Should().NotBeNull();
            serviceProvider.GetService<IHtmlGenerator>().Should().NotBeNull();
            serviceProvider.GetService<IPdfConverter>().Should().NotBeNull();
            serviceProvider.GetService<IHtml2PdfService>().Should().NotBeNull();
        }

        [Fact]
        public void AddHtml2Pdf_WithOptions_ShouldConfigureBrowserPool()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            
            var expectedMinSize = 2;
            var expectedMaxSize = 10;

            // Act
            services.AddHtml2Pdf(options =>
            {
                options.MinInstances = expectedMinSize;
                options.MaxInstances = expectedMaxSize;
            });

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            var browserPoolOptions = serviceProvider.GetService<IOptions<BrowserPoolOptions>>()?.Value;
            
            browserPoolOptions.Should().NotBeNull();
            browserPoolOptions!.MinInstances.Should().Be(expectedMinSize);
            browserPoolOptions.MaxInstances.Should().Be(expectedMaxSize);
        }

        [Fact]
        public void AddHtml2Pdf_MultipleCalls_ShouldNotDuplicateServices()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddHtml2Pdf();
            services.AddHtml2Pdf();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            // Should not throw due to multiple registrations
            serviceProvider.GetService<IHtml2PdfService>().Should().NotBeNull();
            serviceProvider.GetService<IBrowserPool>().Should().NotBeNull();
        }

        [Fact]
        public void AddHtml2Pdf_ServicesHaveCorrectLifetime()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddHtml2Pdf();

            // Assert
            var serviceDescriptors = services.ToList();
            
            // Browser pool should be singleton
            var browserPoolDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IBrowserPool));
            browserPoolDescriptor.Should().NotBeNull();
            browserPoolDescriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
            
            // Other services should be scoped
            var html2PdfServiceDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IHtml2PdfService));
            html2PdfServiceDescriptor.Should().NotBeNull();
            html2PdfServiceDescriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        }

        [Fact]
        public void AddHtml2Pdf_ShouldResolveServicesCorrectly()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddHtml2Pdf();

            // Assert
            var serviceProvider = services.BuildServiceProvider();
            
            using var scope = serviceProvider.CreateScope();
            var html2PdfService = scope.ServiceProvider.GetService<IHtml2PdfService>();
            
            html2PdfService.Should().NotBeNull();
            html2PdfService.Should().BeOfType<Html2PdfService>();
        }

        [Fact]
        public void AddHtml2Pdf_WithCustomLifetime_ShouldUseSpecifiedLifetime()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();

            // Act
            services.AddHtml2PdfWithLifetime(
                options => { },
                ServiceLifetime.Singleton,
                ServiceLifetime.Singleton);

            // Assert
            var serviceDescriptors = services.ToList();
            
            var htmlGeneratorDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IHtmlGenerator));
            htmlGeneratorDescriptor.Should().NotBeNull();
            htmlGeneratorDescriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
            
            var pdfConverterDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IPdfConverter));
            pdfConverterDescriptor.Should().NotBeNull();
            pdfConverterDescriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }
    }
}
