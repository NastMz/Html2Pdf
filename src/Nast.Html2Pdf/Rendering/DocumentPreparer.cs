using System.Text;
using System.Text.RegularExpressions;
using Nast.Html2Pdf.Security;
using Nast.Html2Pdf.Templating;

namespace Nast.Html2Pdf.Rendering;

/// <summary>Turns a <see cref="PdfSource"/> into what the renderer loads, enforcing source-level policy.</summary>
internal sealed partial class DocumentPreparer(LocalFileGuard files, NetworkPolicy network, TemplateRenderer templates)
{
    public async Task<PreparedDocument> PrepareAsync(PdfSource source, PageGeometry geometry, string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object? templateModel = null;
        Uri documentUrl;
        byte[]? inline = null;
        var localAccess = false;

        switch (source)
        {
            case HtmlSource html:
                (documentUrl, localAccess) = InlineDocumentUrl(html.BaseDirectory, operationId);
                inline = Encoding.UTF8.GetBytes(html.Html);
                break;

            case TemplateSource template:
                templateModel = template.Model;
                string text;
                string? baseDirectory = template.Options.BaseDirectory;
                if (template.TemplatePath is { } path)
                {
                    var file = Resolve(path, expectDirectory: false);
                    var read = files.Read(file);
                    if (read.Status != LocalReadStatus.Success)
                    {
                        throw read.Status == LocalReadStatus.Denied
                            ? new PdfConversionException(PdfConversionError.AccessDenied, "The template file is outside the allowed local roots.")
                            : new PdfConversionException(PdfConversionError.ResourceUnavailable, "The template file could not be read.");
                    }

                    text = DecodeUtf8(read.Content!);
                    baseDirectory ??= Path.GetDirectoryName(file.FullPath);
                }
                else
                {
                    text = template.Template!;
                }

                (documentUrl, localAccess) = InlineDocumentUrl(baseDirectory, operationId);
                var rendered = await RenderTemplateAsync(text, template.Model, template.Options.Partials, null).ConfigureAwait(false);
                inline = Encoding.UTF8.GetBytes(rendered);
                break;

            case FileSource fileSource:
                var document = Resolve(fileSource.Path, expectDirectory: false);
                documentUrl = document.ToUrl();
                localAccess = true;
                break;

            case UrlSource url:
                if (!network.AllowUrlSources)
                {
                    throw new PdfConversionException(PdfConversionError.AccessDenied, "URL sources are disabled (Network.AllowUrlSources).");
                }

                if (network.Deny(url.Url) is { } reason)
                {
                    throw new PdfConversionException(PdfConversionError.AccessDenied, $"The URL is not allowed by the network policy: {Redaction.Url(url.Url.AbsoluteUri)} ({reason}).");
                }

                documentUrl = url.Url;
                break;

            default:
                throw new PdfConversionException(PdfConversionError.InvalidRequest, "Unsupported source type.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var options = geometry.Options;
        var bandModel = options.HeaderFooterModel ?? templateModel;
        string? header = null, footer = null, headerMeasure = null, footerMeasure = null;
        if (options.Header is not null)
        {
            var content = await RenderBandAsync(options.Header.Template, bandModel, "header").ConfigureAwait(false);
            header = geometry.BandHtml(content, header: true, forMeasurement: false);
            headerMeasure = geometry.BandHtml(content, header: true, forMeasurement: true);
        }

        if (options.Footer is not null)
        {
            var content = await RenderBandAsync(options.Footer.Template, bandModel, "footer").ConfigureAwait(false);
            footer = geometry.BandHtml(content, header: false, forMeasurement: false);
            footerMeasure = geometry.BandHtml(content, header: false, forMeasurement: true);
        }

        var prepared = new PreparedDocument
        {
            DocumentUrl = documentUrl,
            LocalAccess = localAccess,
            HeaderHtml = header,
            FooterHtml = footer,
            BandMeasurementHtml = header is null && footer is null
                ? null
                : $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0\">{headerMeasure}{footerMeasure}</body></html>",
        };
        if (inline is not null)
        {
            prepared.InlineDocuments[documentUrl.AbsoluteUri] = inline;
        }

        return prepared;
    }

    private (Uri Url, bool LocalAccess) InlineDocumentUrl(string? baseDirectory, string operationId)
    {
        if (baseDirectory is null)
        {
            return (LocalOrigins.DocumentUrl(operationId), false);
        }

        var directory = Resolve(baseDirectory, expectDirectory: true);
        var directoryUrl = directory.RelativePath.Length == 0
            ? LocalOrigins.RootBase(directory.RootIndex)
            : new Uri(directory.ToUrl().AbsoluteUri + "/");
        return (new Uri(directoryUrl, $"__html2pdf-{operationId}.html"), true);
    }

    private LocalFile Resolve(string path, bool expectDirectory)
        => files.ResolveInput(path, expectDirectory, out var error, out var reason)
           ?? throw new PdfConversionException(error!.Value, reason!);

    private async Task<string> RenderBandAsync(string template, object? model, string name)
    {
        var values = new Dictionary<string, object>
        {
            ["pdf"] = new Dictionary<string, object>
            {
                ["page_number"] = new TrustedHtml("<span class=\"pageNumber\"></span>"),
                ["total_pages"] = new TrustedHtml("<span class=\"totalPages\"></span>"),
            },
        };
        var html = await RenderTemplateAsync(template, model, null, values).ConfigureAwait(false);
        if (ScriptTag().IsMatch(html))
        {
            throw new PdfConversionException(PdfConversionError.InvalidRequest, $"The {name} template must not contain scripts.");
        }

        return html;
    }

    private async Task<string> RenderTemplateAsync(string template, object? model, IReadOnlyDictionary<string, string>? partials, IReadOnlyDictionary<string, object>? values)
    {
        try
        {
            return await templates.RenderAsync(template, model, partials, values).ConfigureAwait(false);
        }
        catch (TemplateRenderException ex)
        {
            throw new PdfConversionException(PdfConversionError.TemplateError, ex.Message);
        }
    }

    private static string DecodeUtf8(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            throw new PdfConversionException(PdfConversionError.TemplateError, "Template files must be encoded as UTF-8.");
        }
    }

    [GeneratedRegex(@"<\s*script\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTag();
}
