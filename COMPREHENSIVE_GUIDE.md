# Nast.Html2Pdf - Complete User Guide

## Table of Contents

1. [Introduction](#introduction)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [Configuration Options](#configuration-options)
5. [Usage Examples](#usage-examples)
6. [Advanced Features](#advanced-features)
7. [Performance Optimization](#performance-optimization)
8. [Troubleshooting](#troubleshooting)
9. [API Reference](#api-reference)
10. [Best Practices](#best-practices)

## Introduction

Nast.Html2Pdf is a powerful .NET library that converts HTML content to PDF documents. It combines the flexibility of RazorLight templates with the reliability of PuppeteerSharp for high-quality PDF generation.

**Important Note**: This library uses **RazorLight** for template processing, not Handlebars. All template examples in this guide use RazorLight/Razor syntax (`@Model.Property`, `@foreach`, `@if`, etc.).

### Key Features

- **Template-based PDF generation** using RazorLight syntax
- **Browser pool optimization** for high-performance scenarios
- **Flexible configuration** for PDF layout and styling
- **Complete dependency injection support**
- **Comprehensive logging and diagnostics**
- **Real-world document support** (invoices, reports, letters)

### Requirements

- .NET 8.0 or later
- Windows, macOS, or Linux
- Minimum 512MB RAM available
- Internet connection for initial Chromium download

## Installation

### Package Manager Console

```powershell
Install-Package Nast.Html2Pdf
```

### .NET CLI

```bash
dotnet add package Nast.Html2Pdf
```

### Package Reference

```xml
<PackageReference Include="Nast.Html2Pdf" Version="1.0.0" />
```

### Post-Installation Setup

On first use, PuppeteerSharp will automatically download the required Chromium binaries (~170MB). This happens once per machine.

## Quick Start

### 1. Basic Setup

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Extensions;
using Nast.Html2Pdf.Abstractions;

// Configure services
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddHtml2Pdf();

var serviceProvider = services.BuildServiceProvider();
var pdfService = serviceProvider.GetRequiredService<IHtml2PdfService>();
```

### 2. Generate Your First PDF

```csharp
// Simple HTML to PDF
var html = @"
<!DOCTYPE html>
<html>
<head>
    <title>My First PDF</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        h1 { color: #333; }
    </style>
</head>
<body>
    <h1>Hello, World!</h1>
    <p>This is my first PDF generated with Nast.Html2Pdf!</p>
</body>
</html>";

var result = await pdfService.GeneratePdfFromHtmlAsync(html);

if (result.Success)
{
    await File.WriteAllBytesAsync("my-first-pdf.pdf", result.Data);
    Console.WriteLine($"PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
}
else
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}
```

### 3. Using Templates with Data

```csharp
// RazorLight template
var template = @"
<!DOCTYPE html>
<html>
<body>
    <h1>Welcome, @Model.CustomerName!</h1>
    <p>Your order #@Model.OrderNumber has been confirmed.</p>
    <ul>
        @foreach(var item in Model.Items)
        {
            <li>@item.Name - $@item.Price.ToString("F2")</li>
        }
    </ul>
    <p><strong>Total: $@Model.Total.ToString("F2")</strong></p>
</body>
</html>";

// Data model
var data = new
{
    CustomerName = "John Doe",
    OrderNumber = "ORD-2025-001",
    Items = new[]
    {
        new { Name = "Product A", Price = 29.99m },
        new { Name = "Product B", Price = 49.99m }
    },
    Total = 79.98m
};

var result = await pdfService.GeneratePdfAsync(template, data);
```

## Configuration Options

### Browser Pool Configuration

```csharp
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 1;        // Minimum browser instances
    options.MaxInstances = 5;        // Maximum browser instances
    options.MaxLifetimeMinutes = 60; // Max lifetime in minutes
    options.AcquireTimeoutSeconds = 30; // Timeout to acquire instance
    options.Headless = true;         // Run in headless mode
    options.AdditionalArgs = new[] { "--no-sandbox", "--disable-dev-shm-usage" };
});
```

### PDF Layout Options

```csharp
var pdfOptions = new PdfOptions
{
    Format = "A4",                   // Page format: A4, A3, Letter, Legal, etc.
    Landscape = false,               // Portrait or landscape orientation
    PrintBackground = true,          // Include background colors and images
    Scale = 1.0f,                    // Scale factor (0.1 to 2.0)

    // Custom margins
    Margins = new PdfMargins
    {
        Top = "2cm",
        Bottom = "2cm",
        Left = "1.5cm",
        Right = "1.5cm"
    },

    // Custom page size (overrides Format)
    Width = 8.5f,                    // Width in inches
    Height = 11.0f,                  // Height in inches

    // Page ranges
    PageRanges = "1-3,5",            // Print specific pages

    // Performance options
    WaitForImages = true,            // Wait for images to load
    TimeoutMs = 30000                // Page load timeout
};
```

### HTML Generation Options

```csharp
var htmlOptions = new HtmlGenerationOptions
{
    BasePath = "/path/to/templates",  // Base path for relative resources
    InlineStyles = true,              // Include inline CSS
    Encoding = "UTF-8",               // HTML encoding
    IncludeViewport = true,           // Include viewport meta tag
    AdditionalCss = "body { margin: 0; }", // Additional CSS
    AdditionalJs = "console.log('PDF generated');" // Additional JavaScript
};
```

## Usage Examples

### Invoice Generation

```csharp
public class InvoiceService
{
    private readonly IHtml2PdfService _pdfService;

    public InvoiceService(IHtml2PdfService pdfService)
    {
        _pdfService = pdfService;
    }

    public async Task<byte[]> GenerateInvoiceAsync(InvoiceData invoice)
    {
        var template = @"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; }
        .header { display: flex; justify-content: space-between; margin-bottom: 30px; }
        .invoice-number { font-size: 24px; font-weight: bold; }
        .company-info { text-align: right; }
        .items-table { width: 100%; border-collapse: collapse; margin: 20px 0; }
        .items-table th, .items-table td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        .items-table th { background-color: #f2f2f2; }
        .total { text-align: right; font-size: 18px; font-weight: bold; }
    </style>
</head>
<body>
    <div class='header'>
        <div>
            <h1>INVOICE</h1>
            <p>Invoice #: @Model.InvoiceNumber</p>
            <p>Date: @Model.Date.ToString("yyyy-MM-dd")</p>
        </div>
        <div class='company-info'>
            <h2>@Model.Company.Name</h2>
            <p>@Model.Company.Address</p>
            <p>@Model.Company.Phone</p>
        </div>
    </div>

    <div>
        <h3>Bill To:</h3>
        <p>@Model.Customer.Name</p>
        <p>@Model.Customer.Address</p>
    </div>

    <table class='items-table'>
        <thead>
            <tr>
                <th>Description</th>
                <th>Quantity</th>
                <th>Price</th>
                <th>Total</th>
            </tr>
        </thead>
        <tbody>
            @foreach(var item in Model.Items)
            {
                <tr>
                    <td>@item.Description</td>
                    <td>@item.Quantity</td>
                    <td>$@item.Price.ToString("F2")</td>
                    <td>$@item.Total.ToString("F2")</td>
                </tr>
            }
        </tbody>
    </table>

    <div class='total'>
        <p>Subtotal: $@Model.Subtotal.ToString("F2")</p>
        <p>Tax: $@Model.Tax.ToString("F2")</p>
        <p>Total: $@Model.Total.ToString("F2")</p>
    </div>
</body>
</html>";

        var pdfOptions = new PdfOptions
        {
            Format = "A4",
            PrintBackground = true,
            Header = new PdfHeaderFooter
            {
                Template = "<div style='font-size: 10px; text-align: center;'>@Model.Company.Name - Invoice @Model.InvoiceNumber</div>",
                Height = "1cm"
            },
            Footer = new PdfHeaderFooter
            {
                Template = "<div style='font-size: 10px; text-align: center;'>Page <span class='pageNumber'></span> of <span class='totalPages'></span></div>",
                Height = "1cm"
            }
        };

        var result = await _pdfService.GeneratePdfAsync(template, invoice, pdfOptions);

        if (!result.Success)
        {
            throw new Exception($"Failed to generate invoice: {result.ErrorMessage}");
        }

        return result.Data;
    }
}
```

### Report Generation with Charts

```csharp
public async Task<byte[]> GenerateReportWithChartsAsync(ReportData data)
{
    var template = @"
<!DOCTYPE html>
<html>
<head>
    <script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .chart-container { width: 100%; height: 400px; margin: 20px 0; }
        .summary-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; }
        .summary-card { background: #f5f5f5; padding: 20px; border-radius: 8px; text-align: center; }
    </style>
</head>
<body>
    <h1>Monthly Sales Report</h1>

    <div class='summary-grid'>
        <div class='summary-card'>
            <h3>Total Sales</h3>
            <p style='font-size: 24px; font-weight: bold;'>$@Model.TotalSales.ToString("N0")</p>
        </div>
        <div class='summary-card'>
            <h3>Orders</h3>
            <p style='font-size: 24px; font-weight: bold;'>@Model.TotalOrders</p>
        </div>
        <div class='summary-card'>
            <h3>Growth</h3>
            <p style='font-size: 24px; font-weight: bold;'>@Model.Growth.ToString("P1")</p>
        </div>
    </div>

    <div class='chart-container'>
        <canvas id='salesChart'></canvas>
    </div>

    <script>
        const ctx = document.getElementById('salesChart').getContext('2d');
        new Chart(ctx, {
            type: 'line',
            data: {
                labels: [@foreach(var month in Model.MonthlyData) { <text>'@month.Month'@(month != Model.MonthlyData.Last() ? "," : "")</text> }],
                datasets: [{
                    label: 'Sales',
                    data: [@foreach(var data in Model.MonthlyData) { <text>@data.Sales@(data != Model.MonthlyData.Last() ? "," : "")</text> }],
                    borderColor: 'rgb(75, 192, 192)',
                    tension: 0.1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false
            }
        });
    </script>
</body>
</html>";

    var pdfOptions = new PdfOptions
    {
        Format = "A4",
        PrintBackground = true,
        WaitForImages = true,
        TimeoutMs = 60000 // Longer timeout for chart rendering
    };

    var result = await _pdfService.GeneratePdfAsync(template, data, pdfOptions);
    return result.Data;
}
```

### File-based Templates

```csharp
// Save template to file
var templatePath = Path.Combine("Templates", "letter-template.html");
await File.WriteAllTextAsync(templatePath, letterTemplate);

// Generate PDF from file
var result = await pdfService.GeneratePdfFromFileAsync(templatePath, letterData);
```

### URL to PDF Conversion

```csharp
// Convert a webpage to PDF
var result = await pdfService.GeneratePdfFromUrlAsync("https://example.com");
```

## Advanced Features

### Custom Headers and Footers

```csharp
var pdfOptions = new PdfOptions
{
    Header = new PdfHeaderFooter
    {
        Template = @"
            <div style='font-size: 10px; width: 100%; text-align: center; border-bottom: 1px solid #ccc; padding-bottom: 5px;'>
                <span style='float: left;'>@Model.Company.Name</span>
                <span>@Model.DocumentTitle</span>
                <span style='float: right;'>@Model.Date.ToString("yyyy-MM-dd")</span>
            </div>",
        Height = "1.5cm"
    },
    Footer = new PdfHeaderFooter
    {
        Template = @"
            <div style='font-size: 10px; width: 100%; text-align: center; border-top: 1px solid #ccc; padding-top: 5px;'>
                <span style='float: left;'>Confidential</span>
                <span>Page <span class='pageNumber'></span> of <span class='totalPages'></span></span>
                <span style='float: right;'>Generated: @DateTime.Now</span>
            </div>",
        Height = "1.5cm"
    }
};
```

### Conditional Content with RazorLight

```csharp
var template = @"
@if(Model.IsUrgent)
{
    <div style='background: red; color: white; padding: 10px; margin-bottom: 20px;'>
        URGENT: This document requires immediate attention!
    </div>
}

@foreach(var item in Model.Items)
{
    <div class='item'>
        <h3>@item.Name</h3>
        <p>@item.Description</p>
        @if(item.OnSale)
        {
            <span class='sale-price'>$@item.SalePrice.ToString("F2")</span>
            <span class='original-price'>$@item.OriginalPrice.ToString("F2")</span>
        }
        else
        {
            <span class='price'>$@item.Price.ToString("F2")</span>
        }
    </div>
}

@if(!Model.Items.Any())
{
    <p>No items available.</p>
}
";
```

### Batch Processing

```csharp
public async Task<Dictionary<string, byte[]>> GenerateMultiplePdfsAsync(
    Dictionary<string, object> documents)
{
    var results = new Dictionary<string, byte[]>();
    var tasks = documents.Select(async kvp =>
    {
        var result = await _pdfService.GeneratePdfAsync(template, kvp.Value);
        return new { Key = kvp.Key, Data = result.Data };
    });

    var completedTasks = await Task.WhenAll(tasks);

    foreach (var task in completedTasks)
    {
        results[task.Key] = task.Data;
    }

    return results;
}
```

## Performance Optimization

### Browser Pool Settings

```csharp
// For high-volume scenarios
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 3;        // Keep 3 browsers warm
    options.MaxInstances = 10;       // Allow up to 10 concurrent browsers
    options.MaxLifetimeMinutes = 30; // Recycle browsers after 30 minutes
    options.AcquireTimeoutSeconds = 15; // Shorter timeout for faster failure
});
```

### Memory Management

```csharp
// Dispose of service provider when done
using var serviceProvider = services.BuildServiceProvider();
var pdfService = serviceProvider.GetRequiredService<IHtml2PdfService>();

// Process documents in batches
const int batchSize = 10;
for (int i = 0; i < documents.Count; i += batchSize)
{
    var batch = documents.Skip(i).Take(batchSize);
    await ProcessBatchAsync(batch);

    // Force garbage collection between batches
    GC.Collect();
    GC.WaitForPendingFinalizers();
}
```

### Caching Templates

```csharp
public class CachedTemplateService
{
    private readonly IMemoryCache _cache;
    private readonly IHtml2PdfService _pdfService;

    public async Task<byte[]> GeneratePdfAsync(string templateKey, object data)
    {
        var template = await _cache.GetOrCreateAsync(templateKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await LoadTemplateAsync(templateKey);
        });

        var result = await _pdfService.GeneratePdfAsync(template, data);
        return result.Data;
    }
}
```

## Troubleshooting

### Common Issues

#### 1. Chromium Download Issues

```csharp
// Manual Chromium download
var browserFetcher = new BrowserFetcher();
await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
```

#### 2. Font Issues

```csharp
// Include web fonts in your template
var template = @"
<head>
    <link href='https://fonts.googleapis.com/css2?family=Roboto:wght@300;400;700&display=swap' rel='stylesheet'>
    <style>
        body { font-family: 'Roboto', Arial, sans-serif; }
    </style>
</head>";
```

#### 3. Large Document Timeouts

```csharp
var pdfOptions = new PdfOptions
{
    TimeoutMs = 120000,  // 2 minutes
    WaitForImages = true
};
```

#### 4. Memory Issues

```csharp
// Monitor memory usage
var initialMemory = GC.GetTotalMemory(false);
var result = await pdfService.GeneratePdfAsync(template, data);
var finalMemory = GC.GetTotalMemory(true);
Console.WriteLine($"Memory used: {(finalMemory - initialMemory) / 1024 / 1024}MB");
```

### Debugging

```csharp
// Enable detailed logging
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

// Access diagnostic information
var result = await pdfService.GeneratePdfAsync(template, data);
Console.WriteLine($"Execution time: {result.Duration}");
Console.WriteLine($"Success: {result.Success}");
if (!result.Success)
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}
```

## API Reference

### IHtml2PdfService Interface

```csharp
public interface IHtml2PdfService
{
    Task<PdfResult> GeneratePdfAsync(string template, object? model = null,
        PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null);

    Task<PdfResult> GeneratePdfFromFileAsync(string templatePath, object? model = null,
        PdfOptions? pdfOptions = null, HtmlGenerationOptions? htmlOptions = null);

    Task<PdfResult> GeneratePdfFromHtmlAsync(string html, PdfOptions? pdfOptions = null);

    Task<PdfResult> GeneratePdfFromUrlAsync(string url, PdfOptions? pdfOptions = null);
}
```

### PdfResult Class

```csharp
public class PdfResult
{
    public bool Success { get; set; }
    public byte[] Data { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    public TimeSpan Duration { get; set; }
}
```

### Configuration Classes

- `PdfOptions`: PDF generation options
- `HtmlGenerationOptions`: HTML processing options
- `BrowserPoolOptions`: Browser pool configuration
- `PdfMargins`: Page margin settings
- `PdfHeaderFooter`: Header/footer configuration

## Best Practices

### 1. Template Design

- Use semantic HTML5 elements
- Include proper DOCTYPE declaration
- Use CSS for styling, avoid inline styles when possible
- Test templates in a browser before converting to PDF

### 2. Performance

- Use browser pool for high-volume scenarios
- Cache compiled templates
- Process documents in batches
- Monitor memory usage

### 3. Error Handling

- Always check `PdfResult.Success` before using data
- Log errors for debugging
- Implement retry logic for transient failures
- Validate input data before processing

### 4. Security

- Sanitize user input in templates
- Validate URLs before conversion
- Use HTTPS for external resources
- Implement proper authentication for web-based templates

### 5. Maintenance

- Keep templates versioned
- Monitor browser pool health
- Update Chromium regularly
- Test with different document sizes

## Support and Resources

- **GitHub Repository**: [Html2Pdf](https://github.com/NastMz/Html2Pdf)
- **NuGet Package**: [Nast.Html2Pdf](https://www.nuget.org/packages/Nast.Html2Pdf)
- **Issue Tracker**: [Issues](https://github.com/NastMz/Html2Pdf/issues)
- **License**: MIT

For additional help, please create an issue on the GitHub repository with:

- .NET version
- Package version
- Minimal reproduction code
- Error messages and logs
- Expected vs actual behavior

---

**Last updated**: January 2025
