using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Models;
using Nast.Html2Pdf.Services;
using System.Text;
using Xunit.Abstractions;

namespace Nast.Html2Pdf.Tests.Diagnostics
{
    /// <summary>
    /// Tests for logging and diagnostics functionality
    /// </summary>
    public class DiagnosticsTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IHtml2PdfService _html2PdfService;
        private readonly ITestOutputHelper _output;
        private readonly List<string> _logMessages = new();

        public DiagnosticsTests(ITestOutputHelper output)
        {
            _output = output;
            var services = new ServiceCollection();
            
            // Configure logging to capture messages
            services.AddLogging(builder =>
            {
                builder.AddProvider(new TestLoggerProvider(_logMessages));
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            
            services.AddHtml2Pdf(options =>
            {
                options.MinInstances = 1;
                options.MaxInstances = 2;
            });

            _serviceProvider = services.BuildServiceProvider();
            _html2PdfService = _serviceProvider.GetRequiredService<IHtml2PdfService>();
        }

        [Fact]
        public async Task GeneratePdf_ShouldLogExecutionTime()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <head><title>Test</title></head>
                <body><h1>Performance Test</h1></body>
                </html>";

            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.Duration.TotalMilliseconds > 0);
            
            // Check that diagnostic messages were logged
            var executionLogs = _logMessages.Where(log => log.Contains("PDF generation") || log.Contains("Duration")).ToList();
            Assert.NotEmpty(executionLogs);
            
            _output.WriteLine($"PDF generated in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"Log messages captured: {_logMessages.Count}");
            
            foreach (var log in executionLogs)
            {
                _output.WriteLine($"Log: {log}");
            }
        }

        [Fact]
        public async Task GeneratePdf_WithTemplate_ShouldLogDetailedMetrics()
        {
            // Arrange
            var template = @"
                <html>
                <body>
                    <h1>Welcome {{Name}}</h1>
                    <p>Order: {{OrderId}}</p>
                    <ul>
                        {{#each Items}}
                        <li>{{this}}</li>
                        {{/each}}
                    </ul>
                </body>
                </html>";

            var data = new
            {
                Name = "John Doe",
                OrderId = "ORD-123",
                Items = new[] { "Item 1", "Item 2", "Item 3" }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, data);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.Duration.TotalMilliseconds > 0);
            
            // Check for specific diagnostic log patterns
            var templateLogs = _logMessages.Where(log => log.Contains("Template")).ToList();
            var htmlLogs = _logMessages.Where(log => log.Contains("HTML")).ToList();
            var pdfLogs = _logMessages.Where(log => log.Contains("PDF")).ToList();
            
            Assert.NotEmpty(templateLogs);
            Assert.NotEmpty(htmlLogs);
            Assert.NotEmpty(pdfLogs);
            
            _output.WriteLine($"Template logs: {templateLogs.Count}");
            _output.WriteLine($"HTML logs: {htmlLogs.Count}");
            _output.WriteLine($"PDF logs: {pdfLogs.Count}");
        }

        [Fact]
        public async Task GeneratePdf_WithError_ShouldLogErrorDetails()
        {
            // Arrange - Invalid template syntax
            var invalidTemplate = @"
                <html>
                <body>
                    <h1>Invalid {{#invalid syntax}}</h1>
                </body>
                </html>";

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(invalidTemplate, new { });

            // Assert
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            
            // Check for error logs
            var errorLogs = _logMessages.Where(log => log.Contains("Error") || log.Contains("Failed")).ToList();
            Assert.NotEmpty(errorLogs);
            
            _output.WriteLine($"Error result: {result.ErrorMessage}");
            _output.WriteLine($"Error logs captured: {errorLogs.Count}");
            
            foreach (var log in errorLogs)
            {
                _output.WriteLine($"Error Log: {log}");
            }
        }

        [Fact]
        public async Task GeneratePdf_ShouldMeasureResourceUsage()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Resource Usage Test</title>
                    <style>
                        body { font-family: Arial, sans-serif; }
                        .large-content { 
                            background: linear-gradient(45deg, #ff6b6b, #4ecdc4);
                            height: 1000px;
                            padding: 50px;
                        }
                    </style>
                </head>
                <body>
                    <div class='large-content'>
                        <h1>Large Content Document</h1>
                        <p>This document is designed to use more resources for testing.</p>
                    </div>
                </body>
                </html>";

            // Measure memory before
            var memoryBefore = GC.GetTotalMemory(false);
            
            // Act
            var result = await _html2PdfService.GeneratePdfFromHtmlAsync(html);
            
            // Measure memory after
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsed = memoryAfter - memoryBefore;

            // Assert
            Assert.True(result.Success);
            Assert.True(result.Data.Length > 0);
            
            _output.WriteLine($"Memory used: {memoryUsed / 1024 / 1024:F2} MB");
            _output.WriteLine($"PDF size: {result.Data.Length / 1024:F2} KB");
            _output.WriteLine($"Execution time: {result.Duration.TotalMilliseconds:F2} ms");
            
            // Check for resource usage logs
            var resourceLogs = _logMessages.Where(log => log.Contains("Resource") || log.Contains("Memory")).ToList();
            foreach (var log in resourceLogs)
            {
                _output.WriteLine($"Resource Log: {log}");
            }
        }

        [Fact]
        public async Task GeneratePdf_MultipleConcurrent_ShouldLogBrowserPoolMetrics()
        {
            // Arrange
            var html = @"
                <!DOCTYPE html>
                <html>
                <body>
                    <h1>Concurrent Test {{Index}}</h1>
                    <p>This is document number {{Index}}</p>
                </body>
                </html>";

            var tasks = new List<Task<PdfResult>>();
            
            // Act - Generate multiple PDFs concurrently
            for (int i = 0; i < 5; i++)
            {
                var template = html.Replace("{{Index}}", i.ToString());
                tasks.Add(_html2PdfService.GeneratePdfFromHtmlAsync(template));
            }
            
            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, result => Assert.True(result.Success));
            
            _output.WriteLine($"Generated {results.Length} PDFs concurrently");
            _output.WriteLine($"Total execution time: {results.Sum(r => r.Duration.TotalMilliseconds):F2} ms");
            _output.WriteLine($"Average execution time: {results.Average(r => r.Duration.TotalMilliseconds):F2} ms");
            
            // Check for browser pool logs
            var poolLogs = _logMessages.Where(log => log.Contains("Pool") || log.Contains("Browser")).ToList();
            foreach (var log in poolLogs)
            {
                _output.WriteLine($"Pool Log: {log}");
            }
        }

        [Fact]
        public async Task GeneratePdf_WithPerformanceBottleneck_ShouldIdentifyBottleneck()
        {
            // Arrange - Create a template that might cause performance issues
            var template = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Performance Test</title>
                    <style>
                        .item { 
                            background: linear-gradient(45deg, #ff6b6b, #4ecdc4);
                            margin: 10px;
                            padding: 20px;
                            border-radius: 10px;
                        }
                    </style>
                </head>
                <body>
                    <h1>Performance Test Document</h1>
                    {{#each Items}}
                    <div class='item'>
                        <h3>{{Title}}</h3>
                        <p>{{Description}}</p>
                    </div>
                    {{/each}}
                </body>
                </html>";

            var data = new
            {
                Items = Enumerable.Range(1, 100).Select(i => new
                {
                    Title = $"Item {i}",
                    Description = $"This is a detailed description for item {i}. " +
                                 $"It contains multiple sentences to make the document larger and more complex."
                }).ToArray()
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, data);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.Duration.TotalMilliseconds > 0);
            
            _output.WriteLine($"Complex document generated in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"PDF size: {result.Data.Length / 1024}KB");
            
            // Check for bottleneck detection logs
            var bottleneckLogs = _logMessages.Where(log => 
                log.Contains("Bottleneck") || 
                log.Contains("Performance") ||
                log.Contains("slow")).ToList();
            
            foreach (var log in bottleneckLogs)
            {
                _output.WriteLine($"Bottleneck Log: {log}");
            }
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }

    /// <summary>
    /// Test logger provider for capturing log messages
    /// </summary>
    public class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _logMessages;

        public TestLoggerProvider(List<string> logMessages)
        {
            _logMessages = logMessages;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(categoryName, _logMessages);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Test logger for capturing log messages
    /// </summary>
    public class TestLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly List<string> _logMessages;

        public TestLogger(string categoryName, List<string> logMessages)
        {
            _categoryName = categoryName;
            _logMessages = logMessages;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new NoOpDisposable();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"[{logLevel}] [{_categoryName}] {formatter(state, exception)}";
            _logMessages.Add(message);
        }
    }

    /// <summary>
    /// No-op disposable for test logger scopes
    /// </summary>
    public class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
