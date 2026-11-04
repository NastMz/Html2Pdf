# Templates

Templates use [Liquid](https://shopify.github.io/liquid/) as implemented by [Fluid](https://github.com/sebastienros/fluid).
A template is **trusted** code written by the application; the model passed with it is **untrusted** data.

```csharp
var source = PdfSource.FromTemplate("""
    <h1>Invoice {{ Number }}</h1>
    <p>{{ Customer.Name }}</p>
    <table>
      {% for line in Lines %}
        <tr><td>{{ line.Description }}</td><td>{{ line.Total | format_number: 'N2' }}</td></tr>
      {% endfor %}
    </table>
    {% if Notes %}<p>{{ Notes }}</p>{% endif %}
    """, invoice);
```

## Model access

- Public properties of the model are available at the root, with their .NET names (`{{ Customer.Name }}`).
  Anonymous types, records, classes, dictionaries (`IDictionary<string, object>`) and collections are supported.
- Methods cannot be called, so templates cannot execute .NET code.
- Referencing a value that does not exist (`{{ Custmer.Name }}`) is a `TemplateError`, not an empty string, so typos
  do not produce silently incomplete documents. A property that exists with a `null` value renders as empty.
- Formatting filters (`format_number`, `format_date`, …) use `Html2PdfOptions.Templates.Culture` (invariant by default).

## Escaping

Every `{{ value }}` from the model is HTML-encoded. `<b>` becomes `&lt;b&gt;`, quotes are encoded, and text such as
`{{ Secret }}` or `{% include 'x' %}` inside a value is printed literally — model data is never parsed as template code.

To insert markup that **your application** produced and trusts, wrap it in `TrustedHtml`:

```csharp
var model = new { Body = new TrustedHtml(sanitizedHtmlFromYourCms), Title = userSuppliedTitle };
```

The Liquid `raw` filter is intentionally unavailable, so trusted markup is always an explicit decision in code. Unknown
filters are rejected when the template is parsed.

Escaping is correct for HTML text and quoted attribute values. Do not interpolate untrusted values into `<script>` or
`<style>` blocks, event-handler attributes, unquoted attributes, or URL attributes (`href`, `src`) without validating
them.

## Partials

```csharp
var options = new TemplateSourceOptions
{
    Partials = new Dictionary<string, string>
    {
        ["address"] = "<address>{{ street }}<br>{{ city }}</address>",
    },
};
PdfSource.FromTemplate("{% render 'address', street: Customer.Street, city: Customer.City %}", invoice, options);
```

Partials are resolved only from this trusted dictionary — never from the file system or the model. `{% render %}` gives
the partial an isolated scope; `{% include %}` shares the caller's scope. Nesting is bounded by
`Templates.MaxRecursion`, and loops by `Templates.MaxSteps`.

## Template files and resources

`PdfSource.FromTemplateFile(path, model)` reads a UTF-8 template from an allowed root. Relative URLs in the rendered
document (`<img src="logo.png">`, `<link href="css/print.css">`) resolve against the template's directory, and
root-relative URLs (`/assets/logo.png`) against its root. For in-memory templates, set
`TemplateSourceOptions.BaseDirectory` to enable local resources.

## Headers and footers

Header and footer templates use the same engine and escaping. They are rendered with
`PdfConversionOptions.HeaderFooterModel`, or the document's model for template sources. Two extra values are available:

| Value | Output |
|---|---|
| `{{ pdf.page_number }}` | Current page number |
| `{{ pdf.total_pages }}` | Total page count |

Header/footer content is rendered by Chromium separately from the document: document styles do not apply (style it
inline or with a `<style>` element in the template), scripts are rejected, and images must be embedded as `data:` URIs.
The default text style is 10pt sans-serif.
