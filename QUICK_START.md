# Nast.Html2Pdf - Installation & Usage Guide

## Installation

### NuGet Package Manager

```powershell
Install-Package Nast.Html2Pdf
```

### .NET CLI

```bash
dotnet add package Nast.Html2Pdf
```

## Basic Usage

### 1. Configure Services

```csharp
using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddHtml2Pdf();

var serviceProvider = services.BuildServiceProvider();
var pdfService = serviceProvider.GetRequiredService<IHtml2PdfService>();
```

### 2. Generate PDF from HTML

```csharp
var html = @"
<!DOCTYPE html>
<html>
<head>
    <title>Sample Document</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .header { background: #f0f0f0; padding: 20px; }
    </style>
</head>
<body>
    <div class='header'>
        <h1>Sample Document</h1>
    </div>
    <p>This is a sample PDF document.</p>
</body>
</html>";

var result = await pdfService.GeneratePdfFromHtmlAsync(html);

if (result.Success)
{
    await File.WriteAllBytesAsync("document.pdf", result.Data);
}
```

### 3. Generate PDF from Template

```csharp
var template = @"
<html>
<body>
    <h1>Invoice #@Model.InvoiceNumber</h1>
    <p>Customer: @Model.CustomerName</p>
    <p>Date: @Model.Date.ToString("yyyy-MM-dd")</p>
    <table>
        <tr><th>Item</th><th>Price</th></tr>
        @foreach(var item in Model.Items)
        {
            <tr><td>@item.Name</td><td>$@item.Price.ToString("F2")</td></tr>
        }
    </table>
    <p><strong>Total: $@Model.Total.ToString("F2")</strong></p>
</body>
</html>";

var data = new
{
    InvoiceNumber = "INV-001",
    CustomerName = "John Doe",
    Date = DateTime.Now,
    Items = new[]
    {
        new { Name = "Product A", Price = 50.00m },
        new { Name = "Product B", Price = 30.00m }
    },
    Total = 80.00m
};

var result = await pdfService.GeneratePdfAsync(template, data);
```

## Configuration Options

### PDF Options

```csharp
var pdfOptions = new PdfOptions
{
    Format = "A4",
    PrintBackground = true,
    Margins = new PdfMargins
    {
        Top = "1cm",
        Bottom = "1cm",
        Left = "1cm",
        Right = "1cm"
    }
};

var result = await pdfService.GeneratePdfFromHtmlAsync(html, pdfOptions);
```

### Browser Pool Configuration

```csharp
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 1;
    options.MaxInstances = 5;
    options.MaxLifetimeMinutes = 60;
    options.AcquireTimeoutSeconds = 30;
});
```

## Error Handling

```csharp
var result = await pdfService.GeneratePdfAsync(template, data);

if (!result.Success)
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
    if (result.Exception != null)
    {
        Console.WriteLine($"Exception: {result.Exception}");
    }
}
else
{
    Console.WriteLine($"PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
    Console.WriteLine($"PDF size: {result.Data.Length} bytes");
}
```

## Features

- **Template Support**: Use RazorLight syntax for dynamic content
- **High Performance**: Browser pool optimization for concurrent operations
- **Flexible Configuration**: Extensive PDF layout and styling options
- **Diagnostics**: Built-in logging and performance monitoring
- **Real-world Ready**: Supports complex documents like invoices and reports

## Support

For more information, examples, and troubleshooting, see the [Comprehensive Guide](COMPREHENSIVE_GUIDE.md).
