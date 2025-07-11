# Nast.Html2Pdf

A .NET library for converting HTML to PDF using RazorLight for template generation and Playwright for PDF conversion.

## Features

- ✅ **HTML Generation**: Dynamic template support with RazorLight
- ✅ **PDF Conversion**: Using Playwright for high-quality conversion
- ✅ **Browser Pool**: Performance optimization with instance reuse
- ✅ **Flexible Configuration**: Customization of sizes, margins, orientation
- ✅ **Headers & Footers**: Support for content in headers and footers
- ✅ **Error Handling**: Complete error management with detailed information
- ✅ **External Resources**: Support for fonts, images, and CSS styles
- ✅ **Dependency Injection**: Complete integration with .NET DI

## Installation

```bash
dotnet add package Nast.Html2Pdf
```

## Basic Usage

### Simple Usage (Without DI)

```csharp
using Nast.Html2Pdf;
using Nast.Html2Pdf.Helpers;

// Create the service
var html2PdfService = Html2PdfFactory.CreateDefault();

// Generate PDF from HTML
var htmlTemplate = @"
    <html>
    <body>
        <h1>Hello @Model.Name</h1>
        <p>Date: @DateTime.Now.ToString(""dd/MM/yyyy"")</p>
    </body>
    </html>";

var model = new { Name = "John Doe" };
var result = await html2PdfService.GeneratePdfAsync(htmlTemplate, model);

if (result.Success)
{
    File.WriteAllBytes("output.pdf", result.Data);
    Console.WriteLine($"PDF generated successfully. Size: {result.Size} bytes");
}
else
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}
```

### Usage with Configuration

```csharp
using Nast.Html2Pdf.Helpers;

// Configure PDF options
var pdfOptions = PdfOptionsHelper.A4Standard;
pdfOptions.Landscape = true;
pdfOptions.Header = new PdfHeaderFooter
{
    Template = "<div style='text-align: center;'>My Company</div>",
    Height = "2cm"
};

// Generate PDF
var result = await html2PdfService.GeneratePdfAsync(htmlTemplate, model, pdfOptions);
```

### Usage with Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Nast.Html2Pdf.Extensions;

// Configure services
var services = new ServiceCollection();
services.AddLogging();
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 2;
    options.MaxInstances = 10;
    options.MaxLifetimeMinutes = 60;
    options.Headless = true;
});

var serviceProvider = services.BuildServiceProvider();
var html2PdfService = serviceProvider.GetRequiredService<IHtml2PdfService>();
```

## Usage Examples

### Generate Invoice

```csharp
var invoiceTemplate = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Invoice</title>
    </head>
    <body>
        <div class='invoice-header'>
            <h1>INVOICE</h1>
            <p>Number: @Model.Number</p>
            <p>Date: @Model.Date.ToString(""dd/MM/yyyy"")</p>
        </div>

        <div class='invoice-details'>
            <h3>Customer:</h3>
            <p>@Model.Customer.Name</p>
            <p>@Model.Customer.Address</p>
        </div>

        <table class='invoice-table'>
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
                        <td>@item.Price.ToString(""C"")</td>
                        <td>@item.Total.ToString(""C"")</td>
                    </tr>
                }
            </tbody>
        </table>

        <div class='invoice-total'>
            <h3>Total: @Model.Total.ToString(""C"")</h3>
        </div>
    </body>
    </html>";

var invoice = new
{
    Number = "INV-001",
    Date = DateTime.Now,
    Customer = new { Name = "John Doe", Address = "123 Main St, City" },
    Items = new[]
    {
        new { Description = "Product 1", Quantity = 2, Price = 100.00m, Total = 200.00m },
        new { Description = "Product 2", Quantity = 1, Price = 150.00m, Total = 150.00m }
    },
    Total = 350.00m
};

var result = await html2PdfService.GeneratePdfAsync(
    invoiceTemplate,
    invoice,
    PdfOptionsHelper.Invoice,
    HtmlOptionsHelper.Invoice
);
```

### Generate Report

```csharp
var reportTemplate = @"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Sales Report</title>
    </head>
    <body>
        <div class='report-header'>
            <h1>SALES REPORT</h1>
            <p>Period: @Model.StartDate.ToString(""dd/MM/yyyy"") - @Model.EndDate.ToString(""dd/MM/yyyy"")</p>
        </div>

        <div class='report-summary'>
            <h3>Summary</h3>
            <p>Total Sales: @Model.TotalSales.ToString(""C"")</p>
            <p>Number of Transactions: @Model.TransactionCount</p>
        </div>

        <div class='report-section'>
            <h3>Sales Detail</h3>
            <table class='report-table'>
                <thead>
                    <tr>
                        <th>Date</th>
                        <th>Customer</th>
                        <th>Product</th>
                        <th>Quantity</th>
                        <th>Amount</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach(var sale in Model.Sales)
                    {
                        <tr>
                            <td>@sale.Date.ToString(""dd/MM/yyyy"")</td>
                            <td>@sale.Customer</td>
                            <td>@sale.Product</td>
                            <td>@sale.Quantity</td>
                            <td>@sale.Amount.ToString(""C"")</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </body>
    </html>";

var report = new
{
    StartDate = DateTime.Now.AddDays(-30),
    EndDate = DateTime.Now,
    TotalSales = 5000.00m,
    TransactionCount = 25,
    Sales = new[]
    {
        new { Date = DateTime.Now.AddDays(-5), Customer = "John Doe", Product = "Product A", Quantity = 2, Amount = 200.00m },
        new { Date = DateTime.Now.AddDays(-3), Customer = "Jane Smith", Product = "Product B", Quantity = 1, Amount = 150.00m },
        // more data...
    }
};

var result = await html2PdfService.GeneratePdfAsync(
    reportTemplate,
    report,
    PdfOptionsHelper.Report,
    HtmlOptionsHelper.Report
);
```

## Predefined Configurations

### PDF Options

```csharp
// Available configurations
var a4Standard = PdfOptionsHelper.A4Standard;
var a4Landscape = PdfOptionsHelper.A4Landscape;
var letterStandard = PdfOptionsHelper.LetterStandard;
var noMargins = PdfOptionsHelper.NoMargins;
var invoice = PdfOptionsHelper.Invoice;
var report = PdfOptionsHelper.Report;

// Custom configuration
var custom = PdfOptionsHelper.WithHeaderAndFooter(
    headerTemplate: "<div>My Header</div>",
    footerTemplate: "<div>My Footer</div>"
);

// Custom size
var customSize = PdfOptionsHelper.WithCustomSize(8.5f, 11f); // Letter size
```

### HTML Options

```csharp
// Available configurations
var standard = HtmlOptionsHelper.Standard;
var invoice = HtmlOptionsHelper.Invoice;
var report = HtmlOptionsHelper.Report;

// Custom CSS
var withCustomCss = HtmlOptionsHelper.WithCustomCss(@"
    body { font-family: 'Times New Roman', serif; }
    .highlight { background-color: yellow; }
");
```

## Extension Methods

```csharp
// Save directly to file
await html2PdfService.GeneratePdfToFileAsync(template, model, "output.pdf");

// Get as Stream
var stream = await html2PdfService.GeneratePdfToStreamAsync(template, model);

// From template file
await html2PdfService.GeneratePdfFromFileToFileAsync("template.html", model, "output.pdf");

// From URL
await html2PdfService.GeneratePdfFromUrlToFileAsync("https://example.com", "output.pdf");
```

## Error Handling

```csharp
var result = await html2PdfService.GeneratePdfAsync(template, model);

if (result.Success)
{
    Console.WriteLine($"PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
    Console.WriteLine($"Size: {result.Size} bytes");

    // Use result.Data
    File.WriteAllBytes("output.pdf", result.Data);
}
else
{
    Console.WriteLine($"Error: {result.ErrorMessage}");

    if (result.Exception != null)
    {
        Console.WriteLine($"Exception: {result.Exception.Message}");
    }
}
```

## Advanced Configuration

### Browser Pool

```csharp
services.AddHtml2Pdf(options =>
{
    options.MinInstances = 2;          // Minimum 2 instances
    options.MaxInstances = 10;         // Maximum 10 instances
    options.MaxLifetimeMinutes = 60;   // 1 hour lifetime
    options.AcquireTimeoutSeconds = 30; // 30 seconds timeout
    options.Headless = true;           // Run without GUI
    options.AdditionalArgs = new[] { "--no-sandbox", "--disable-dev-shm-usage" };
});
```

### Lifetime Configuration

```csharp
services.AddHtml2PdfWithLifetime(
    configureBrowserPool: options => { /* configuration */ },
    browserPoolLifetime: ServiceLifetime.Singleton,
    serviceLifetime: ServiceLifetime.Scoped
);
```

## Advanced Templates

### With External Resources

```csharp
var templateWithResources = @"
    <!DOCTYPE html>
    <html>
    <head>
        <style>
            @import url('https://fonts.googleapis.com/css2?family=Roboto:wght@300;400;700&display=swap');
            body { font-family: 'Roboto', sans-serif; }
        </style>
    </head>
    <body>
        <img src='https://via.placeholder.com/300x200' alt='Image' />
        <h1>Title with external font</h1>
    </body>
    </html>";

var options = new PdfOptions
{
    WaitForImages = true,  // Wait for images to load
    TimeoutMs = 30000     // 30 seconds timeout
};
```

### With Page Breaks

```csharp
var templateWithPageBreaks = @"
    <style>
        .page-break { page-break-after: always; }
    </style>

    <div>
        <h1>Page 1</h1>
        <p>Content of the first page</p>
    </div>

    <div class='page-break'></div>

    <div>
        <h1>Page 2</h1>
        <p>Content of the second page</p>
    </div>";
```

## License

This project is licensed under the MIT License. See the LICENSE file for more details.
