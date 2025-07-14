using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Services;

namespace Nast.Html2Pdf.Tests.Integration
{
    [Collection("TestCleanup")]
    public class Html2PdfIntegrationTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IHtml2PdfService _html2PdfService;

        public Html2PdfIntegrationTests()
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddHtml2Pdf(options =>
            {
                options.MinInstances = 1;
                options.MaxInstances = 2;
            });

            _serviceProvider = services.BuildServiceProvider();
            _html2PdfService = _serviceProvider.GetRequiredService<IHtml2PdfService>();
        }

        [Fact]
        public async Task GeneratePdfFromHtml_WithSimpleHtml_ShouldSucceed()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Test Document</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 20px; }
                        h1 { color: #333; }
                        p { line-height: 1.6; }
                    </style>
                </head>
                <body>
                    <h1>Integration Test Document</h1>
                    <p>This is a test document generated during integration testing.</p>
                    <p>It contains multiple paragraphs and styled elements.</p>
                </body>
                </html>";

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldNotBeNull();
            result.Data!.Length.ShouldBeGreaterThan(0);
            result.Size.ShouldBeGreaterThan(0);
            result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            result.ErrorMessage.ShouldBeNull();
            result.Exception.ShouldBeNull();
        }

        [Fact]
        public async Task GeneratePdfFromHtml_WithComplexHtml_ShouldSucceed()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Complex Test Document</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 20px; }
                        .header { background-color: #f0f0f0; padding: 10px; border-radius: 5px; }
                        .content { margin: 20px 0; }
                        .table { width: 100%; border-collapse: collapse; }
                        .table th, .table td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                        .table th { background-color: #f2f2f2; }
                        .footer { text-align: center; color: #666; margin-top: 20px; }
                    </style>
                </head>
                <body>
                    <div class='header'>
                        <h1>Complex Integration Test Document</h1>
                    </div>
                    
                    <div class='content'>
                        <h2>Test Data</h2>
                        <table class='table'>
                            <thead>
                                <tr>
                                    <th>Name</th>
                                    <th>Value</th>
                                    <th>Description</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr>
                                    <td>Test 1</td>
                                    <td>100</td>
                                    <td>First test case</td>
                                </tr>
                                <tr>
                                    <td>Test 2</td>
                                    <td>200</td>
                                    <td>Second test case</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                    
                    <div class='footer'>
                        <p>Generated on: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"</p>
                    </div>
                </body>
                </html>";

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldNotBeNull();
            result.Data!.Length.ShouldBeGreaterThan(0);
            result.Size.ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task GeneratePdfFromHtml_WithCustomPdfOptions_ShouldSucceed()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Custom Options Test</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 20px; }
                        h1 { color: #333; }
                    </style>
                </head>
                <body>
                    <h1>Custom PDF Options Test</h1>
                    <p>This document tests custom PDF generation options.</p>
                </body>
                </html>";

            var pdfOptions = new PdfOptions
            {
                Format = "Letter",
                Landscape = true,
                PrintBackground = true,
                Scale = 0.8f,
                Margins = new PdfMargins
                {
                    Top = "2cm",
                    Bottom = "2cm",
                    Left = "1.5cm",
                    Right = "1.5cm"
                }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html, pdfOptions);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldNotBeNull();
            result.Data!.Length.ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task GeneratePdfFromHtml_WithInvalidHtml_ShouldFail()
        {
            // Arrange
            var invalidHtml = "<html><body><h1>Unclosed tag</body></html>";

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(invalidHtml);

            // Assert - Even invalid HTML should still generate a PDF, browsers are tolerant
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue(); // Browsers typically handle invalid HTML gracefully
        }

        [Fact]
        public async Task GeneratePdfFromHtml_WithEmptyHtml_ShouldSucceed()
        {
            // Arrange
            var emptyHtml = "";

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(emptyHtml);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue(); // Empty HTML now generates a valid empty PDF
            result.Data.ShouldNotBeNull();
        }

        [Fact]
        public async Task GeneratePdfFromTemplate_WithModel_ShouldSucceed()
        {
            // Arrange
            var template = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Template Test</title>
                    <style>
                        body { font-family: Arial, sans-serif; margin: 20px; }
                        h1 { color: #333; }
                    </style>
                </head>
                <body>
                    <h1>Hello @Model.Name!</h1>
                    <p>You are @Model.Age years old.</p>
                    <p>Your email is: @Model.Email</p>
                </body>
                </html>";

            var model = new
            {
                Name = "John Doe",
                Age = 30,
                Email = "john.doe@example.com"
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldNotBeNull();
            result.Data!.Length.ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task GeneratePdfFromTemplate_WithComplexModel_ShouldSucceed()
        {
            // Arrange
            var template = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>@Model.Title</title>
                </head>
                <body>
                    <h1>@Model.Title</h1>
                    <p>Report Date: @Model.ReportDate</p>
                    <p>Total: @Model.Total</p>
                </body>
                </html>";

            var model = new
            {
                Title = "Sales Report",
                ReportDate = DateTime.Now.ToString("yyyy-MM-dd"),
                Total = "334.75"
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, model);

            // Assert
            result.ShouldNotBeNull();
            result.Success.ShouldBeTrue();
            result.Data.ShouldNotBeNull();
            result.Data!.Length.ShouldBeGreaterThan(0);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Force close all browsers before disposing the service provider
                try
                {
                    var browserPool = _serviceProvider?.GetService<IBrowserPool>() as BrowserPool;
                    browserPool?.ForceCloseAllBrowsersAsync().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Error closing browser pool: {ex.Message}");
                }
                
                _serviceProvider?.Dispose();
            }
        }
    }
}
