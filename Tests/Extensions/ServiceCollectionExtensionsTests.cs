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
            
            serviceProvider.GetService<IBrowserPool>().ShouldNotBeNull();
            serviceProvider.GetService<IHtmlGenerator>().ShouldNotBeNull();
            serviceProvider.GetService<IPdfConverter>().ShouldNotBeNull();
            serviceProvider.GetService<IHtml2PdfService>().ShouldNotBeNull();
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
            
            browserPoolOptions.ShouldNotBeNull();
            browserPoolOptions!.MinInstances.ShouldBe(expectedMinSize);
            browserPoolOptions.MaxInstances.ShouldBe(expectedMaxSize);
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
            serviceProvider.GetService<IHtml2PdfService>().ShouldNotBeNull();
            serviceProvider.GetService<IBrowserPool>().ShouldNotBeNull();
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
            browserPoolDescriptor.ShouldNotBeNull();
            browserPoolDescriptor!.Lifetime.ShouldBe(ServiceLifetime.Singleton);
            
            // Other services should be scoped
            var html2PdfServiceDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IHtml2PdfService));
            html2PdfServiceDescriptor.ShouldNotBeNull();
            html2PdfServiceDescriptor!.Lifetime.ShouldBe(ServiceLifetime.Scoped);
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
            
            html2PdfService.ShouldNotBeNull();
            html2PdfService.ShouldBeOfType<Html2PdfService>();
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
            htmlGeneratorDescriptor.ShouldNotBeNull();
            htmlGeneratorDescriptor!.Lifetime.ShouldBe(ServiceLifetime.Singleton);
            
            var pdfConverterDescriptor = serviceDescriptors.FirstOrDefault(s => s.ServiceType == typeof(IPdfConverter));
            pdfConverterDescriptor.ShouldNotBeNull();
            pdfConverterDescriptor!.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        }

        [Fact]
        public void AddHtml2Pdf_ShouldFailFastWhenBrowserPoolOptionsAreInvalid()
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                services.AddHtml2Pdf(options =>
                {
                    options.MinInstances = 3;
                    options.MaxInstances = 2;
                }));

            exception.ParamName.ShouldBe("minInstances");
            exception.Message.ShouldContain("less than or equal to maxInstances");
        }

        [Theory]
        [InlineData(0, "acquireTimeoutSeconds")]
        [InlineData(-1, "acquireTimeoutSeconds")]
        public void AddHtml2Pdf_ShouldRejectNonPositiveAcquireTimeout(int acquireTimeoutSeconds, string expectedParamName)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                services.AddHtml2Pdf(options =>
                {
                    options.AcquireTimeoutSeconds = acquireTimeoutSeconds;
                }));

            exception.ParamName.ShouldBe(expectedParamName);
            exception.Message.ShouldContain("greater than zero");
        }

        [Theory]
        [InlineData((ServiceLifetime)(-1))]
        [InlineData((ServiceLifetime)99)]
        public void AddHtml2PdfWithLifetime_ShouldRejectInvalidBrowserPoolLifetime(ServiceLifetime browserPoolLifetime)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                services.AddHtml2PdfWithLifetime(
                    options => { },
                    browserPoolLifetime,
                    ServiceLifetime.Scoped));

            exception.ParamName.ShouldBe("browserPoolLifetime");
            exception.Message.ShouldContain("must be a valid ServiceLifetime value");
        }

        [Theory]
        [InlineData((ServiceLifetime)(-1))]
        [InlineData((ServiceLifetime)99)]
        public void AddHtml2PdfWithLifetime_ShouldRejectInvalidServiceLifetime(ServiceLifetime serviceLifetime)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                services.AddHtml2PdfWithLifetime(
                    options => { },
                    ServiceLifetime.Singleton,
                    serviceLifetime));

            exception.ParamName.ShouldBe("serviceLifetime");
            exception.Message.ShouldContain("must be a valid ServiceLifetime value");
        }

        [Theory]
        [InlineData(0, "maxLifetimeMinutes")]
        [InlineData(-5, "maxLifetimeMinutes")]
        public void AddHtml2Pdf_ShouldRejectNonPositiveMaxLifetime(int maxLifetimeMinutes, string expectedParamName)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
                services.AddHtml2Pdf(options =>
                {
                    options.MaxLifetimeMinutes = maxLifetimeMinutes;
                }));

            exception.ParamName.ShouldBe(expectedParamName);
            exception.Message.ShouldContain("greater than zero");
        }
    }
}
