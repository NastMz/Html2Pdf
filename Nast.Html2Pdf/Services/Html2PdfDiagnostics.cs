using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Nast.Html2Pdf.Services
{
    /// <summary>
    /// Enhanced logging and diagnostics service for Html2Pdf operations
    /// </summary>
    public class Html2PdfDiagnostics
    {
        private readonly ILogger<Html2PdfDiagnostics> _logger;
        private readonly DiagnosticSource _diagnosticSource;

        /// <summary>
        /// Initializes a new instance of the Html2PdfDiagnostics class
        /// </summary>
        /// <param name="logger">The logger instance</param>
        public Html2PdfDiagnostics(ILogger<Html2PdfDiagnostics> logger)
        {
            _logger = logger;
            _diagnosticSource = new DiagnosticListener("Nast.Html2Pdf");
        }

        /// <summary>
        /// Logs the start of a PDF generation operation
        /// </summary>
        public IDisposable BeginPdfGeneration(string operationType, object? parameters = null)
        {
            var operationId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;
            
            _logger.LogInformation(
                "PDF Generation Started: {OperationType} | Operation ID: {OperationId} | Started: {StartTime}",
                operationType, operationId, startTime);

            if (parameters != null)
            {
                _logger.LogDebug(
                    "PDF Generation Parameters: {OperationId} | {Parameters}",
                    operationId, JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true }));
            }

            _diagnosticSource.StartActivity(new Activity("PdfGeneration"), new { OperationType = operationType, OperationId = operationId });

            return new PdfGenerationTracker(operationId, operationType, startTime, _logger, _diagnosticSource);
        }

        /// <summary>
        /// Logs HTML generation metrics
        /// </summary>
        public void LogHtmlGenerationMetrics(string operationId, TimeSpan duration, int htmlLength, bool success, string? error = null)
        {
            if (success)
            {
                _logger.LogInformation(
                    "HTML Generation Completed: {OperationId} | Duration: {Duration}ms | HTML Length: {HtmlLength} chars",
                    operationId, duration.TotalMilliseconds, htmlLength);
            }
            else
            {
                _logger.LogError(
                    "HTML Generation Failed: {OperationId} | Duration: {Duration}ms | Error: {Error}",
                    operationId, duration.TotalMilliseconds, error ?? "Unknown error");
            }
        }

        /// <summary>
        /// Logs PDF conversion metrics
        /// </summary>
        public void LogPdfConversionMetrics(string operationId, TimeSpan duration, int pdfSize, bool success, string? error = null)
        {
            if (success)
            {
                _logger.LogInformation(
                    "PDF Conversion Completed: {OperationId} | Duration: {Duration}ms | PDF Size: {PdfSize} bytes",
                    operationId, duration.TotalMilliseconds, pdfSize);
            }
            else
            {
                _logger.LogError(
                    "PDF Conversion Failed: {OperationId} | Duration: {Duration}ms | Error: {Error}",
                    operationId, duration.TotalMilliseconds, error ?? "Unknown error");
            }
        }

        /// <summary>
        /// Logs browser pool metrics
        /// </summary>
        public void LogBrowserPoolMetrics(string operationId, TimeSpan waitTime, int poolSize, int activeInstances)
        {
            _logger.LogDebug(
                "Browser Pool Metrics: {OperationId} | Wait Time: {WaitTime}ms | Pool Size: {PoolSize} | Active: {ActiveInstances}",
                operationId, waitTime.TotalMilliseconds, poolSize, activeInstances);
        }

        /// <summary>
        /// Logs performance bottleneck detection
        /// </summary>
        public void LogPerformanceBottleneck(string operationId, string bottleneckType, TimeSpan duration, string details)
        {
            _logger.LogWarning(
                "Performance Bottleneck Detected: {OperationId} | Type: {BottleneckType} | Duration: {Duration}ms | Details: {Details}",
                operationId, bottleneckType, duration.TotalMilliseconds, details);
        }

        /// <summary>
        /// Logs resource usage metrics
        /// </summary>
        public void LogResourceUsage(string operationId, long memoryUsage, double cpuUsage, TimeSpan totalDuration)
        {
            _logger.LogInformation(
                "Resource Usage: {OperationId} | Memory: {MemoryUsage} MB | CPU: {CpuUsage}% | Total Duration: {TotalDuration}ms",
                operationId, memoryUsage / 1024 / 1024, cpuUsage, totalDuration.TotalMilliseconds);
        }

        /// <summary>
        /// Logs detailed error information
        /// </summary>
        public void LogDetailedError(string operationId, Exception exception, string context, object? additionalData = null)
        {
            if (_logger == null)
            {
                throw new InvalidOperationException("Logger is not initialized. Make sure Html2PdfDiagnostics is properly registered in DI container.");
            }

            operationId = operationId ?? "Unknown";
            context = context ?? "Unknown";
            
            if (exception == null)
            {
                _logger.LogError("LogDetailedError called with null exception for operation {OperationId}", operationId);
                return;
            }

            _logger.LogError(exception,
                "Detailed Error: {OperationId} | Context: {Context} | Exception: {ExceptionType} | Message: {Message}",
                operationId, context, exception.GetType().Name, exception.Message);

            if (additionalData != null)
            {
                try
                {
                    var serialized = JsonSerializer.Serialize(additionalData, new JsonSerializerOptions { WriteIndented = true });
                    _logger.LogDebug(
                        "Additional Error Data: {OperationId} | {AdditionalData}",
                        operationId, serialized);
                }
                catch (Exception serializationEx)
                {
                    _logger.LogDebug(
                        "Additional Error Data (Serialization Failed): {OperationId} | Type: {Type} | SerializationError: {SerializationError}",
                        operationId, additionalData.GetType().Name, serializationEx.Message);
                }
            }
        }

        /// <summary>
        /// Logs template processing metrics
        /// </summary>
        public void LogTemplateProcessing(string operationId, string templateType, int templateSize, TimeSpan processingTime, bool success)
        {
            if (success)
            {
                _logger.LogDebug(
                    "Template Processing: {OperationId} | Type: {TemplateType} | Size: {TemplateSize} chars | Duration: {ProcessingTime}ms",
                    operationId, templateType, templateSize, processingTime.TotalMilliseconds);
            }
            else
            {
                _logger.LogError(
                    "Template Processing Failed: {OperationId} | Type: {TemplateType} | Size: {TemplateSize} chars | Duration: {ProcessingTime}ms",
                    operationId, templateType, templateSize, processingTime.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Logs quality assurance metrics
        /// </summary>
        public void LogQualityMetrics(string operationId, QualityMetrics metrics)
        {
            _logger.LogInformation(
                "Quality Metrics: {OperationId} | Template Validation: {TemplateValid} | HTML Validation: {HtmlValid} | PDF Size: {PdfSize} bytes | Page Count: {PageCount}",
                operationId, metrics.TemplateValid, metrics.HtmlValid, metrics.PdfSize, metrics.PageCount);
        }
    }

    /// <summary>
    /// Tracks PDF generation operation from start to finish
    /// </summary>
    internal class PdfGenerationTracker : IDisposable
    {
        private readonly string _operationId;
        private readonly string _operationType;
        private readonly DateTime _startTime;
        private readonly ILogger _logger;
        private readonly DiagnosticSource _diagnosticSource;
        private readonly Stopwatch _stopwatch;
        private bool _disposed = false;

        public PdfGenerationTracker(string operationId, string operationType, DateTime startTime, 
            ILogger logger, DiagnosticSource diagnosticSource)
        {
            _operationId = operationId;
            _operationType = operationType;
            _startTime = startTime;
            _logger = logger;
            _diagnosticSource = diagnosticSource;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _stopwatch.Stop();
                var endTime = DateTime.UtcNow;
                var duration = _stopwatch.Elapsed;

                _logger.LogInformation(
                    "PDF Generation Completed: {OperationType} | Operation ID: {OperationId} | Duration: {Duration}ms | Started: {StartTime} | Ended: {EndTime}",
                    _operationType, _operationId, duration.TotalMilliseconds, _startTime, endTime);

                _diagnosticSource.StopActivity(Activity.Current, new { OperationId = _operationId, Duration = duration });

                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Quality metrics for PDF generation
    /// </summary>
    public class QualityMetrics
    {
        public bool TemplateValid { get; set; }
        public bool HtmlValid { get; set; }
        public int PdfSize { get; set; }
        public int PageCount { get; set; }
        public bool HasImages { get; set; }
        public bool HasTables { get; set; }
        public bool HasStyling { get; set; }
        public TimeSpan RenderTime { get; set; }
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
    }

    /// <summary>
    /// Performance metrics collector
    /// </summary>
    public class PerformanceMetrics
    {
        public TimeSpan HtmlGenerationTime { get; set; }
        public TimeSpan PdfConversionTime { get; set; }
        public TimeSpan BrowserPoolWaitTime { get; set; }
        public TimeSpan TotalTime { get; set; }
        public long MemoryUsage { get; set; }
        public double CpuUsage { get; set; }
        public int HtmlSize { get; set; }
        public int PdfSize { get; set; }
        public string BottleneckIdentified { get; set; } = string.Empty;
        public Dictionary<string, TimeSpan> DetailedTimings { get; set; } = new();
    }

    /// <summary>
    /// Extension methods for enhanced diagnostics
    /// </summary>
    public static class DiagnosticsExtensions
    {
        /// <summary>
        /// Measures execution time of an operation
        /// </summary>
        public static async Task<(T Result, TimeSpan Duration)> MeasureAsync<T>(this Task<T> task)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await task;
            stopwatch.Stop();
            return (result, stopwatch.Elapsed);
        }

        /// <summary>
        /// Measures execution time of an operation
        /// </summary>
        public static async Task<TimeSpan> MeasureAsync(this Task task)
        {
            var stopwatch = Stopwatch.StartNew();
            await task;
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }

        /// <summary>
        /// Identifies performance bottlenecks
        /// </summary>
        public static string IdentifyBottleneck(this PerformanceMetrics metrics)
        {
            var timings = new Dictionary<string, TimeSpan>
            {
                { "HTML Generation", metrics.HtmlGenerationTime },
                { "PDF Conversion", metrics.PdfConversionTime },
                { "Browser Pool Wait", metrics.BrowserPoolWaitTime }
            };

            var bottleneck = timings.OrderByDescending(kvp => kvp.Value).First();
            
            // Consider something a bottleneck if it takes more than 50% of total time
            if (bottleneck.Value.TotalMilliseconds > metrics.TotalTime.TotalMilliseconds * 0.5)
            {
                return bottleneck.Key;
            }

            return "No significant bottleneck detected";
        }

        /// <summary>
        /// Gets current memory usage
        /// </summary>
        public static long GetCurrentMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// Gets current CPU usage (simplified)
        /// </summary>
        public static double GetCurrentCpuUsage()
        {
            // This is a simplified implementation
            // In a real scenario, you'd use PerformanceCounter or similar
            using var process = Process.GetCurrentProcess();
            return process.TotalProcessorTime.TotalMilliseconds;
        }
    }
}
