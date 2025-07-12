using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Models;
using System.Text;
using Xunit.Abstractions;

namespace Nast.Html2Pdf.Tests.Functional
{
    /// <summary>
    /// Functional tests for real-world scenarios like invoices, reports, and letters
    /// </summary>
    public class RealWorldScenariosTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IHtml2PdfService _html2PdfService;
        private readonly ITestOutputHelper _output;

        public RealWorldScenariosTests(ITestOutputHelper output)
        {
            _output = output;
            var services = new ServiceCollection();
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.AddDebug();
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
        public async Task GenerateInvoice_WithCompleteData_ShouldProduceValidPdf()
        {
            // Arrange
            var invoiceData = new InvoiceModel
            {
                InvoiceNumber = "INV-2025-001",
                Date = DateTime.Now,
                DueDate = DateTime.Now.AddDays(30),
                Company = new CompanyModel
                {
                    Name = "ACME Corporation",
                    Address = "123 Business St, Suite 100",
                    City = "New York",
                    State = "NY",
                    ZipCode = "10001",
                    Phone = "+1 (555) 123-4567",
                    Email = "billing@acme.com"
                },
                Customer = new CustomerModel
                {
                    Name = "John Doe",
                    Address = "456 Customer Ave",
                    City = "Los Angeles",
                    State = "CA",
                    ZipCode = "90210",
                    Email = "john.doe@example.com"
                },
                Items = new List<InvoiceItemModel>
                {
                    new() { Description = "Web Development Services", Quantity = 40, UnitPrice = 75.00m },
                    new() { Description = "SEO Optimization", Quantity = 10, UnitPrice = 120.00m },
                    new() { Description = "Domain Registration", Quantity = 1, UnitPrice = 15.00m }
                },
                Tax = 0.08m,
                Notes = "Payment due within 30 days. Late fees may apply."
            };

            var template = GetInvoiceTemplate();

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
                },
                Header = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-size: 10px; color: #666;'>Invoice {{invoiceNumber}}</div>",
                    Height = "1cm"
                },
                Footer = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-size: 10px; color: #666;'>Page <span class='pageNumber'></span> of <span class='totalPages'></span></div>",
                    Height = "1cm"
                }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, invoiceData, pdfOptions);

            // Assert
            Assert.True(result.Success, $"PDF generation failed: {result.ErrorMessage}");
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Length > 0);
            Assert.True(result.Duration.TotalMilliseconds > 0);
            
            // Validate PDF structure (basic checks)
            var pdfHeader = Encoding.UTF8.GetString(result.Data.Take(100).ToArray());
            Assert.Contains("%PDF", pdfHeader);
            
            _output.WriteLine($"Invoice PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"PDF size: {result.Data.Length} bytes");
            
            // Optional: Save for manual inspection
            await SavePdfForInspection(result.Data, "invoice_test.pdf");
        }

        [Fact]
        public async Task GenerateMonthlyReport_WithChartsAndTables_ShouldProduceValidPdf()
        {
            // Arrange
            var reportData = new MonthlyReportModel
            {
                ReportDate = DateTime.Now,
                CompanyName = "Tech Solutions Inc.",
                Period = "December 2024",
                Summary = new ReportSummaryModel
                {
                    TotalRevenue = 125000.00m,
                    TotalExpenses = 78000.00m,
                    NetProfit = 47000.00m,
                    GrowthPercentage = 15.5m
                },
                Departments = new List<DepartmentPerformanceModel>
                {
                    new() { Name = "Sales", Revenue = 85000.00m, Expenses = 25000.00m, EmployeeCount = 12 },
                    new() { Name = "Marketing", Revenue = 20000.00m, Expenses = 35000.00m, EmployeeCount = 8 },
                    new() { Name = "Development", Revenue = 20000.00m, Expenses = 18000.00m, EmployeeCount = 15 }
                },
                MonthlyData = GenerateMonthlyData()
            };

            var template = GetMonthlyReportTemplate();

            var pdfOptions = new PdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                Margins = new PdfMargins { Top = "2cm", Bottom = "2cm", Left = "1.5cm", Right = "1.5cm" },
                Header = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-weight: bold; font-size: 12px;'>Monthly Performance Report - {{period}}</div>",
                    Height = "1.5cm"
                },
                Footer = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-size: 10px; color: #666;'>Generated on {{currentDate}} | Page <span class='pageNumber'></span></div>",
                    Height = "1.5cm"
                }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, reportData, pdfOptions);

            // Assert
            Assert.True(result.Success, $"PDF generation failed: {result.ErrorMessage}");
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Length > 0);
            
            _output.WriteLine($"Monthly Report PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"PDF size: {result.Data.Length} bytes");
            
            await SavePdfForInspection(result.Data, "monthly_report_test.pdf");
        }

        [Fact]
        public async Task GenerateBusinessLetter_WithLetterhead_ShouldProduceValidPdf()
        {
            // Arrange
            var letterData = new BusinessLetterModel
            {
                Date = DateTime.Now,
                SenderCompany = new CompanyModel
                {
                    Name = "Global Business Solutions",
                    Address = "789 Corporate Blvd",
                    City = "Chicago",
                    State = "IL",
                    ZipCode = "60601",
                    Phone = "+1 (555) 987-6543",
                    Email = "info@globalbiz.com"
                },
                RecipientName = "Ms. Sarah Johnson",
                RecipientTitle = "Director of Operations",
                RecipientCompany = "Johnson & Associates",
                RecipientAddress = "456 Professional Ave, Suite 200\nSeattle, WA 98101",
                Subject = "Proposal for Strategic Partnership",
                Body = @"Dear Ms. Johnson,

I hope this letter finds you well. I am writing to propose a strategic partnership between our organizations that could benefit both parties significantly.

Our company, Global Business Solutions, has been a leader in the consulting industry for over 15 years. We specialize in:
• Digital transformation strategies
• Process optimization
• Technology integration
• Change management

We believe that Johnson & Associates' expertise in market analysis would complement our services perfectly. Together, we could offer our clients a comprehensive solution that addresses both strategic planning and market positioning.

I would welcome the opportunity to discuss this proposal in more detail. Please let me know if you would be available for a meeting in the coming weeks.

Thank you for your time and consideration.

Sincerely,",
                SenderName = "Robert Smith",
                SenderTitle = "Chief Executive Officer",
                Attachments = new List<string> { "Partnership Proposal.pdf", "Company Profile.pdf" }
            };

            var template = GetBusinessLetterTemplate();

            var pdfOptions = new PdfOptions
            {
                Format = "Letter",
                PrintBackground = true,
                Margins = new PdfMargins { Top = "2.5cm", Bottom = "2cm", Left = "2cm", Right = "2cm" }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, letterData, pdfOptions);

            // Assert
            Assert.True(result.Success, $"PDF generation failed: {result.ErrorMessage}");
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Length > 0);
            
            _output.WriteLine($"Business Letter PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"PDF size: {result.Data.Length} bytes");
            
            await SavePdfForInspection(result.Data, "business_letter_test.pdf");
        }

        [Fact]
        public async Task GenerateComplexDocument_WithMultiplePages_ShouldHandlePagination()
        {
            // Arrange
            var complexData = new ComplexDocumentModel
            {
                Title = "Annual Compliance Report",
                Sections = GenerateComplexSections(50) // Generate 50 sections for multiple pages
            };

            var template = GetComplexDocumentTemplate();

            var pdfOptions = new PdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                Header = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-size: 12px; font-weight: bold; border-bottom: 1px solid #ccc; padding-bottom: 5px;'>Annual Compliance Report</div>",
                    Height = "1.5cm"
                },
                Footer = new PdfHeaderFooter
                {
                    Template = "<div style='text-align: center; font-size: 10px; color: #666;'>Page <span class='pageNumber'></span> of <span class='totalPages'></span> | Generated: {{currentDate}}</div>",
                    Height = "1.5cm"
                }
            };

            // Act
            var result = await _html2PdfService.GeneratePdfAsync(template, complexData, pdfOptions);

            // Assert
            Assert.True(result.Success, $"PDF generation failed: {result.ErrorMessage}");
            Assert.NotNull(result.Data);
            Assert.True(result.Data.Length > 10000); // Should be a substantial document
            
            _output.WriteLine($"Complex Document PDF generated successfully in {result.Duration.TotalMilliseconds}ms");
            _output.WriteLine($"PDF size: {result.Data.Length} bytes");
            
            await SavePdfForInspection(result.Data, "complex_document_test.pdf");
        }

        private async Task SavePdfForInspection(byte[] pdfData, string filename)
        {
            try
            {
                var testOutputPath = Path.Combine(Path.GetTempPath(), "Html2PdfTests");
                Directory.CreateDirectory(testOutputPath);
                var filePath = Path.Combine(testOutputPath, filename);
                await File.WriteAllBytesAsync(filePath, pdfData);
                _output.WriteLine($"PDF saved for inspection: {filePath}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Could not save PDF for inspection: {ex.Message}");
            }
        }

        private static string GetInvoiceTemplate()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Invoice {{InvoiceNumber}}</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; background-color: #f5f5f5; }
        .invoice-container { background: white; padding: 30px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 30px; border-bottom: 2px solid #007bff; padding-bottom: 20px; }
        .logo { font-size: 24px; font-weight: bold; color: #007bff; }
        .invoice-info { text-align: right; }
        .invoice-number { font-size: 18px; font-weight: bold; color: #333; }
        .date { color: #666; }
        .parties { display: flex; justify-content: space-between; margin-bottom: 30px; }
        .party { width: 45%; }
        .party-title { font-weight: bold; color: #007bff; margin-bottom: 10px; }
        .items-table { width: 100%; border-collapse: collapse; margin-bottom: 20px; }
        .items-table th, .items-table td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        .items-table th { background-color: #007bff; color: white; }
        .items-table tr:nth-child(even) { background-color: #f9f9f9; }
        .total-section { text-align: right; margin-top: 20px; }
        .total-row { display: flex; justify-content: flex-end; margin-bottom: 5px; }
        .total-label { width: 120px; font-weight: bold; }
        .total-value { width: 100px; text-align: right; }
        .grand-total { font-size: 18px; font-weight: bold; color: #007bff; border-top: 2px solid #007bff; padding-top: 10px; }
        .notes { margin-top: 30px; padding: 15px; background-color: #f8f9fa; border-left: 4px solid #007bff; }
    </style>
</head>
<body>
    <div class='invoice-container'>
        <div class='header'>
            <div class='logo'>INVOICE</div>
            <div class='invoice-info'>
                <div class='invoice-number'>{{InvoiceNumber}}</div>
                <div class='date'>Date: {{Date:yyyy-MM-dd}}</div>
                <div class='date'>Due: {{DueDate:yyyy-MM-dd}}</div>
            </div>
        </div>
        
        <div class='parties'>
            <div class='party'>
                <div class='party-title'>From:</div>
                <div><strong>{{Company.Name}}</strong></div>
                <div>{{Company.Address}}</div>
                <div>{{Company.City}}, {{Company.State}} {{Company.ZipCode}}</div>
                <div>{{Company.Phone}}</div>
                <div>{{Company.Email}}</div>
            </div>
            <div class='party'>
                <div class='party-title'>To:</div>
                <div><strong>{{Customer.Name}}</strong></div>
                <div>{{Customer.Address}}</div>
                <div>{{Customer.City}}, {{Customer.State}} {{Customer.ZipCode}}</div>
                <div>{{Customer.Email}}</div>
            </div>
        </div>
        
        <table class='items-table'>
            <thead>
                <tr>
                    <th>Description</th>
                    <th>Quantity</th>
                    <th>Unit Price</th>
                    <th>Total</th>
                </tr>
            </thead>
            <tbody>
                {{#each Items}}
                <tr>
                    <td>{{Description}}</td>
                    <td>{{Quantity}}</td>
                    <td>${{UnitPrice:F2}}</td>
                    <td>${{Total:F2}}</td>
                </tr>
                {{/each}}
            </tbody>
        </table>
        
        <div class='total-section'>
            <div class='total-row'>
                <div class='total-label'>Subtotal:</div>
                <div class='total-value'>${{Subtotal:F2}}</div>
            </div>
            <div class='total-row'>
                <div class='total-label'>Tax ({{TaxRate:P0}}):</div>
                <div class='total-value'>${{TaxAmount:F2}}</div>
            </div>
            <div class='total-row grand-total'>
                <div class='total-label'>Total:</div>
                <div class='total-value'>${{Total:F2}}</div>
            </div>
        </div>
        
        {{#if Notes}}
        <div class='notes'>
            <strong>Notes:</strong><br>
            {{Notes}}
        </div>
        {{/if}}
    </div>
</body>
</html>";
        }

        private static string GetMonthlyReportTemplate()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Monthly Report - {{Period}}</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background: #f8f9fa; }
        .report-container { background: white; padding: 30px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
        .report-header { text-align: center; margin-bottom: 40px; padding-bottom: 20px; border-bottom: 3px solid #28a745; }
        .report-title { font-size: 28px; font-weight: bold; color: #333; margin-bottom: 10px; }
        .report-subtitle { font-size: 16px; color: #666; }
        .summary-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 20px; margin-bottom: 40px; }
        .summary-card { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 10px; text-align: center; }
        .summary-card h3 { margin: 0 0 10px 0; font-size: 16px; opacity: 0.9; }
        .summary-card .value { font-size: 24px; font-weight: bold; margin-bottom: 5px; }
        .summary-card .change { font-size: 14px; opacity: 0.8; }
        .section-title { font-size: 20px; font-weight: bold; color: #333; margin: 30px 0 15px 0; border-bottom: 2px solid #28a745; padding-bottom: 5px; }
        .dept-table { width: 100%; border-collapse: collapse; margin-bottom: 30px; }
        .dept-table th, .dept-table td { border: 1px solid #ddd; padding: 12px; text-align: left; }
        .dept-table th { background: #28a745; color: white; font-weight: bold; }
        .dept-table tr:nth-child(even) { background: #f8f9fa; }
        .chart-placeholder { height: 200px; background: #f8f9fa; border: 2px dashed #ccc; display: flex; align-items: center; justify-content: center; color: #666; margin: 20px 0; }
    </style>
</head>
<body>
    <div class='report-container'>
        <div class='report-header'>
            <div class='report-title'>{{CompanyName}}</div>
            <div class='report-subtitle'>Monthly Performance Report - {{Period}}</div>
        </div>
        
        <div class='summary-grid'>
            <div class='summary-card'>
                <h3>Total Revenue</h3>
                <div class='value'>${{Summary.TotalRevenue:N0}}</div>
                <div class='change'>+{{Summary.GrowthPercentage:F1}}% from last month</div>
            </div>
            <div class='summary-card'>
                <h3>Total Expenses</h3>
                <div class='value'>${{Summary.TotalExpenses:N0}}</div>
            </div>
            <div class='summary-card'>
                <h3>Net Profit</h3>
                <div class='value'>${{Summary.NetProfit:N0}}</div>
            </div>
            <div class='summary-card'>
                <h3>Profit Margin</h3>
                <div class='value'>{{Summary.ProfitMargin:P1}}</div>
            </div>
        </div>
        
        <div class='section-title'>Department Performance</div>
        <table class='dept-table'>
            <thead>
                <tr>
                    <th>Department</th>
                    <th>Revenue</th>
                    <th>Expenses</th>
                    <th>Profit</th>
                    <th>Employees</th>
                    <th>Profit per Employee</th>
                </tr>
            </thead>
            <tbody>
                {{#each Departments}}
                <tr>
                    <td><strong>{{Name}}</strong></td>
                    <td>${{Revenue:N0}}</td>
                    <td>${{Expenses:N0}}</td>
                    <td>${{Profit:N0}}</td>
                    <td>{{EmployeeCount}}</td>
                    <td>${{ProfitPerEmployee:N0}}</td>
                </tr>
                {{/each}}
            </tbody>
        </table>
        
        <div class='section-title'>Monthly Trend</div>
        <div class='chart-placeholder'>
            [Chart: Revenue vs Expenses Trend - Data visualization would be implemented here]
        </div>
    </div>
</body>
</html>";
        }

        private static string GetBusinessLetterTemplate()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Business Letter</title>
    <style>
        body { font-family: 'Times New Roman', Times, serif; margin: 0; padding: 0; background: white; }
        .letter-container { max-width: 8.5in; margin: 0 auto; padding: 1in; }
        .letterhead { text-align: center; margin-bottom: 30px; padding-bottom: 20px; border-bottom: 3px solid #003366; }
        .company-name { font-size: 24px; font-weight: bold; color: #003366; margin-bottom: 10px; }
        .company-info { font-size: 12px; color: #666; line-height: 1.4; }
        .date { text-align: right; margin-bottom: 30px; font-size: 12px; }
        .recipient { margin-bottom: 30px; font-size: 12px; line-height: 1.6; }
        .subject { font-weight: bold; margin-bottom: 20px; text-decoration: underline; }
        .body { font-size: 12px; line-height: 1.8; margin-bottom: 30px; text-align: justify; }
        .body p { margin-bottom: 15px; }
        .signature-section { margin-top: 40px; }
        .signature-line { border-bottom: 1px solid #000; width: 200px; margin-bottom: 5px; }
        .signature-info { font-size: 12px; line-height: 1.4; }
        .attachments { margin-top: 30px; font-size: 11px; }
        .attachments-title { font-weight: bold; margin-bottom: 10px; }
    </style>
</head>
<body>
    <div class='letter-container'>
        <div class='letterhead'>
            <div class='company-name'>{{SenderCompany.Name}}</div>
            <div class='company-info'>
                {{SenderCompany.Address}}<br>
                {{SenderCompany.City}}, {{SenderCompany.State}} {{SenderCompany.ZipCode}}<br>
                Phone: {{SenderCompany.Phone}} | Email: {{SenderCompany.Email}}
            </div>
        </div>
        
        <div class='date'>{{Date:MMMM dd, yyyy}}</div>
        
        <div class='recipient'>
            {{RecipientName}}<br>
            {{RecipientTitle}}<br>
            {{RecipientCompany}}<br>
            {{RecipientAddress}}
        </div>
        
        <div class='subject'>Re: {{Subject}}</div>
        
        <div class='body'>
            {{#each BodyParagraphs}}
            <p>{{this}}</p>
            {{/each}}
        </div>
        
        <div class='signature-section'>
            <div class='signature-line'></div>
            <div class='signature-info'>
                <strong>{{SenderName}}</strong><br>
                {{SenderTitle}}
            </div>
        </div>
        
        {{#if Attachments}}
        <div class='attachments'>
            <div class='attachments-title'>Attachments:</div>
            {{#each Attachments}}
            <div>• {{this}}</div>
            {{/each}}
        </div>
        {{/if}}
    </div>
</body>
</html>";
        }

        private static string GetComplexDocumentTemplate()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>{{Title}}</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 0; padding: 20px; line-height: 1.6; }
        .document-container { max-width: 8.5in; margin: 0 auto; }
        .title-page { text-align: center; padding: 100px 0; border-bottom: 2px solid #333; margin-bottom: 40px; }
        .document-title { font-size: 32px; font-weight: bold; color: #333; margin-bottom: 20px; }
        .document-subtitle { font-size: 18px; color: #666; }
        .section { margin-bottom: 40px; page-break-inside: avoid; }
        .section-title { font-size: 20px; font-weight: bold; color: #333; margin-bottom: 15px; border-bottom: 1px solid #ccc; padding-bottom: 5px; }
        .section-content { font-size: 14px; text-align: justify; }
        .page-break { page-break-before: always; }
        .table-of-contents { margin-bottom: 40px; }
        .toc-title { font-size: 24px; font-weight: bold; margin-bottom: 20px; }
        .toc-item { margin-bottom: 10px; }
        .toc-item a { text-decoration: none; color: #333; }
        .toc-item .page-number { float: right; }
    </style>
</head>
<body>
    <div class='document-container'>
        <div class='title-page'>
            <div class='document-title'>{{Title}}</div>
            <div class='document-subtitle'>Comprehensive Analysis and Recommendations</div>
        </div>
        
        <div class='table-of-contents'>
            <div class='toc-title'>Table of Contents</div>
            {{#each Sections}}
            <div class='toc-item'>
                <a href='#section-{{@index}}'>{{@index 1}}. {{Title}}</a>
                <span class='page-number'>{{PageNumber}}</span>
            </div>
            {{/each}}
        </div>
        
        {{#each Sections}}
        <div class='section' id='section-{{@index}}'>
            <div class='section-title'>{{@index 1}}. {{Title}}</div>
            <div class='section-content'>
                {{Content}}
            </div>
        </div>
        {{/each}}
    </div>
</body>
</html>";
        }

        private List<MonthlyDataModel> GenerateMonthlyData()
        {
            var random = new Random();
            var data = new List<MonthlyDataModel>();
            
            for (int i = 1; i <= 12; i++)
            {
                data.Add(new MonthlyDataModel
                {
                    Month = new DateTime(2024, i, 1, 0, 0, 0, DateTimeKind.Utc).ToString("MMMM"),
                    Revenue = random.Next(80000, 150000),
                    Expenses = random.Next(60000, 100000)
                });
            }
            
            return data;
        }

        private List<DocumentSectionModel> GenerateComplexSections(int count)
        {
            var sections = new List<DocumentSectionModel>();
            var random = new Random();
            
            var sampleTitles = new[]
            {
                "Executive Summary", "Introduction", "Market Analysis", "Financial Performance",
                "Risk Assessment", "Strategic Recommendations", "Implementation Timeline",
                "Resource Requirements", "Performance Metrics", "Conclusion"
            };
            
            for (int i = 0; i < count; i++)
            {
                var title = i < sampleTitles.Length ? sampleTitles[i] : $"Section {i + 1}";
                var content = GenerateLoremIpsum(random.Next(200, 800));
                
                sections.Add(new DocumentSectionModel
                {
                    Title = title,
                    Content = content,
                    PageNumber = (i / 3) + 1 // Approximate 3 sections per page
                });
            }
            
            return sections;
        }

        private static string GenerateLoremIpsum(int wordCount)
        {
            var words = new[]
            {
                "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
                "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
                "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
                "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
                "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
                "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
                "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
                "deserunt", "mollit", "anim", "id", "est", "laborum"
            };
            
            var random = new Random();
            var result = new StringBuilder();
            
            for (int i = 0; i < wordCount; i++)
            {
                if (i > 0 && i % 15 == 0)
                {
                    result.Append(".</p><p>");
                }
                else if (i > 0)
                {
                    result.Append(" ");
                }
                
                if (i == 0 || (i > 0 && result.ToString().EndsWith("<p>")))
                {
                    result.Append(char.ToUpper(words[random.Next(words.Length)][0]) + words[random.Next(words.Length)][1..]);
                }
                else
                {
                    result.Append(words[random.Next(words.Length)]);
                }
            }
            
            return $"<p>{result}</p>";
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
                _serviceProvider?.Dispose();
            }
        }
    }

    // Model classes for functional tests
    public class InvoiceModel
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public DateTime DueDate { get; set; }
        public CompanyModel Company { get; set; } = new();
        public CustomerModel Customer { get; set; } = new();
        public List<InvoiceItemModel> Items { get; set; } = new();
        public decimal Tax { get; set; }
        public string? Notes { get; set; }
        
        public decimal Subtotal => Items.Sum(i => i.Total);
        public decimal TaxAmount => Subtotal * Tax;
        public decimal Total => Subtotal + TaxAmount;
        public decimal TaxRate => Tax;
    }

    public class CompanyModel
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class CustomerModel
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class InvoiceItemModel
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Total => Quantity * UnitPrice;
    }

    public class MonthlyReportModel
    {
        public DateTime ReportDate { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public ReportSummaryModel Summary { get; set; } = new();
        public List<DepartmentPerformanceModel> Departments { get; set; } = new();
        public List<MonthlyDataModel> MonthlyData { get; set; } = new();
    }

    public class ReportSummaryModel
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit { get; set; }
        public decimal GrowthPercentage { get; set; }
        public decimal ProfitMargin => TotalRevenue > 0 ? NetProfit / TotalRevenue : 0;
    }

    public class DepartmentPerformanceModel
    {
        public string Name { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public int EmployeeCount { get; set; }
        public decimal Profit => Revenue - Expenses;
        public decimal ProfitPerEmployee => EmployeeCount > 0 ? Profit / EmployeeCount : 0;
    }

    public class MonthlyDataModel
    {
        public string Month { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
    }

    public class BusinessLetterModel
    {
        public DateTime Date { get; set; }
        public CompanyModel SenderCompany { get; set; } = new();
        public string RecipientName { get; set; } = string.Empty;
        public string RecipientTitle { get; set; } = string.Empty;
        public string RecipientCompany { get; set; } = string.Empty;
        public string RecipientAddress { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string SenderTitle { get; set; } = string.Empty;
        public List<string> Attachments { get; set; } = new();
        
        public List<string> BodyParagraphs => Body.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public class ComplexDocumentModel
    {
        public string Title { get; set; } = string.Empty;
        public List<DocumentSectionModel> Sections { get; set; } = new();
    }

    public class DocumentSectionModel
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int PageNumber { get; set; }
    }
}
