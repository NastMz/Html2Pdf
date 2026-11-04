namespace Nast.Html2Pdf.Tests.Integration;

/// <summary>Realistic documents used by the integration tests.</summary>
internal static class Documents
{
    public const string InvoiceTemplate = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Invoice {{ Number }}</title>
          <style>
            body { font-family: 'DejaVu Sans', sans-serif; font-size: 11pt; color: #222; }
            h1 { font-size: 20pt; margin: 0 0 4mm; }
            table { width: 100%; border-collapse: collapse; margin-top: 6mm; }
            th, td { border-bottom: 1px solid #ccc; padding: 2mm; text-align: left; }
            td.amount, th.amount { text-align: right; }
            .total { font-weight: bold; font-size: 13pt; }
            .badge { background: #0000ff; color: #fff; padding: 1mm 3mm; }
          </style>
        </head>
        <body>
          <h1>Invoice {{ Number }}</h1>
          <p><span class="badge">{{ Status }}</span></p>
          <p>Bill to: {{ Customer.Name }}<br>{{ Customer.Address }}</p>
          <table>
            <thead><tr><th>Description</th><th class="amount">Qty</th><th class="amount">Unit</th><th class="amount">Total</th></tr></thead>
            <tbody>
            {% for line in Lines %}
              <tr><td>{{ line.Description }}</td><td class="amount">{{ line.Quantity }}</td><td class="amount">{{ line.UnitPrice | format_number: 'N2' }}</td><td class="amount">{{ line.Total | format_number: 'N2' }}</td></tr>
            {% endfor %}
            </tbody>
          </table>
          <p class="total">Grand total: {{ GrandTotal | format_number: 'N2' }} {{ Currency }}</p>
          <p>Questions? <a href="https://billing.example.com/invoices/{{ Number }}">View online</a></p>
          {% if Notes %}<p>{{ Notes }}</p>{% endif %}
        </body>
        </html>
        """;

    public static object InvoiceModel(string customer = "Ada Lovelace", string? notes = null) => new
    {
        Number = "INV-2026-0042",
        Status = "PAID",
        Currency = "EUR",
        Customer = new { Name = customer, Address = "12 Analytical Engine Way, London" },
        Lines = new[]
        {
            new { Description = "Implementation work", Quantity = 10, UnitPrice = 95.5m, Total = 955.0m },
            new { Description = "Support plan", Quantity = 1, UnitPrice = 120m, Total = 120m },
            new { Description = "Café ☕ consulting — naïve façade", Quantity = 2, UnitPrice = 50m, Total = 100m },
        },
        GrandTotal = 1175.0m,
        Notes = notes,
    };

    public static string Report(int rows) => $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Quarterly report</title>
        <style>
          body { font-family: sans-serif; font-size: 10pt; }
          h1, h2 { break-after: avoid; }
          table { width: 100%; border-collapse: collapse; }
          thead { display: table-header-group; }
          tr { break-inside: avoid; }
          td, th { border: 1px solid #999; padding: 1.5mm; }
          .chapter { break-before: page; }
        </style></head>
        <body>
          <h1>Quarterly report</h1>
          <h2>Summary</h2>
          <p>Revenue grew in every region.</p>
          <table><thead><tr><th>Row</th><th>Region</th><th>Value</th></tr></thead><tbody>
          {{string.Concat(Enumerable.Range(1, rows).Select(i => $"<tr><td>Row-{i:D4}</td><td>Region {i % 7}</td><td>{i * 13}</td></tr>"))}}
          </tbody></table>
          <h2 class="chapter">Appendix</h2>
          <p>END-OF-REPORT</p>
        </body></html>
        """;

    public const string Letter = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><style>
          @page { size: A5 landscape; margin: 0; }
          body { margin: 0; font-family: serif; }
          .marker { width: 30mm; height: 15mm; background: #ff0000; }
        </style></head>
        <body><div class="marker"></div><p>Dear reader,</p><p>Sincerely, The Team</p></body></html>
        """;
}
