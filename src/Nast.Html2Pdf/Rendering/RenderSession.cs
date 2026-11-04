using System.Text;
using Nast.Html2Pdf.Diagnostics;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Nast.Html2Pdf.Rendering;

internal enum RenderStage
{
    Queued,
    Preparing,
    Starting,
    Loading,
    Readiness,
    Printing,
    Output,
}

/// <summary>Tracks the current stage so deadline failures can be reported precisely.</summary>
internal sealed class StageTracker
{
    private int _stage;

    public RenderStage Current => (RenderStage)Volatile.Read(ref _stage);

    public void Set(RenderStage stage) => Volatile.Write(ref _stage, (int)stage);
}

/// <summary>
/// One rendering attempt in a fresh, isolated browser context. The context (cookies, storage, cache, service workers)
/// is created for this attempt only and closed when it ends, whatever the outcome.
/// </summary>
internal static class RenderSession
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    public static async Task<byte[]> RunAsync(
        RenderEnvironment environment,
        BrowserInstance browser,
        PreparedDocument document,
        PageGeometry geometry,
        string operationId,
        StageTracker stage,
        List<ConversionWarning> warnings,
        CancellationToken cancellationToken)
    {
        IBrowserContext? context = null;
        ICDPSession? browserSession = null;
        try
        {
            stage.Set(RenderStage.Starting);
            context = await browser.Browser.CreateBrowserContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            browserSession = await browser.Browser.CreateCDPSessionAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            await browserSession.SendAsync("Browser.setDownloadBehavior", new Dictionary<string, object>
            {
                ["behavior"] = "deny",
                ["browserContextId"] = context.Id,
            }).WaitAsync(cancellationToken).ConfigureAwait(false);

            if (document.BandMeasurementHtml is not null)
            {
                await MeasureBandsAsync(environment, context, document, operationId, cancellationToken).ConfigureAwait(false);
            }

            var page = await context.NewPageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

            // Popups (window.open, target=_blank) are not mediated and can stall printing; close them immediately.
            var session = browserSession;
            context.TargetCreated += (_, e) =>
            {
                // Pages created by the library have no opener; anything opened by a document does.
                if (e.Target.Type == TargetType.Page && e.Target.Opener is not null)
                {
                    _ = CloseTargetAsync(session, e.TargetInfo.TargetId);
                }
            };
            var crashed = false;
            page.Error += (_, _) => crashed = true;
            var mediator = new RequestMediator(environment, document, operationId, measurementOnly: false, cancellationToken);
            await ConfigurePageAsync(page, mediator, environment.AllowScripts, geometry.Options.MediaType, cancellationToken).ConfigureAwait(false);

            stage.Set(RenderStage.Loading);
            try
            {
                await page.GoToAsync(document.DocumentUrl.AbsoluteUri, new NavigationOptions
                {
                    WaitUntil = [WaitUntilNavigation.Load],
                    Timeout = 0,
                }).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (NavigationException) when (mediator.MainFailure is not null)
            {
                throw mediator.MainFailure;
            }
            catch (NavigationException) when (mediator.MainDocumentStatus is >= 200 and < 400 && !cancellationToken.IsCancellationRequested
                                              && browser.Browser.IsConnected && !crashed)
            {
                // The document itself was served; a later navigation attempt (blocked by the mediator) aborted the
                // navigation watcher. Readiness polling below waits for the document's own load.
            }
            catch (NavigationException) when (!cancellationToken.IsCancellationRequested && browser.Browser.IsConnected && !crashed)
            {
                throw new PdfConversionException(PdfConversionError.ResourceUnavailable, "The document could not be loaded.");
            }

            if (mediator.MainFailure is not null)
            {
                throw mediator.MainFailure;
            }

            if (mediator.MainDocumentStatus is not { } status || status >= 400)
            {
                throw new PdfConversionException(PdfConversionError.ResourceUnavailable, "The document could not be loaded.");
            }

            mediator.LockNavigation();

            stage.Set(RenderStage.Readiness);
            await WaitForReadinessAsync(page, mediator, geometry.Options.Readiness, cancellationToken).ConfigureAwait(false);
            ThrowIfCrashed(crashed);
            var processedIssues = 0;
            ApplyResourcePolicy(mediator, geometry.Options.ResourceFailures, warnings, ref processedIssues);

            if (geometry.AuthoritativePageCss is { } css)
            {
                await page.EvaluateFunctionAsync(
                    "css => { const s = document.createElement('style'); s.textContent = css; (document.head || document.documentElement).appendChild(s); }",
                    css).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            stage.Set(RenderStage.Printing);
            var pdfOptions = geometry.ToPdfOptions(document.HeaderHtml, document.FooterHtml);
            pdfOptions.Timeout = 0;
            byte[] pdf;
            try
            {
                pdf = await page.PdfDataAsync(pdfOptions).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PuppeteerException ex) when (ex.Message.Contains("Page range", StringComparison.OrdinalIgnoreCase)
                                                || ex.Message.Contains("page ranges", StringComparison.OrdinalIgnoreCase))
            {
                throw new PdfConversionException(PdfConversionError.InvalidRequest, "PageRanges selects pages that the document does not have.");
            }

            ThrowIfCrashed(crashed);
            await EnsureOriginalDocumentAsync(page, mediator, cancellationToken).ConfigureAwait(false);
            ValidatePdf(pdf);

            // Requests that started after the readiness check (rare) must still respect the policy.
            ApplyResourcePolicy(mediator, geometry.Options.ResourceFailures, warnings, ref processedIssues);
            return pdf;
        }
        finally
        {
            if (context is not null)
            {
                await CloseContextAsync(environment, browser, context).ConfigureAwait(false);
            }

            if (browserSession is not null)
            {
                try
                {
                    await browserSession.DetachAsync().WaitAsync(CleanupTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The session ends with the browser.
                }
            }
        }
    }

    private static async Task ConfigurePageAsync(IPage page, RequestMediator mediator, bool allowScripts, CssMediaType mediaType, CancellationToken cancellationToken)
    {
        await page.SetJavaScriptEnabledAsync(allowScripts).WaitAsync(cancellationToken).ConfigureAwait(false);
        await page.EmulateMediaTypeAsync(mediaType == CssMediaType.Print ? MediaType.Print : MediaType.Screen).WaitAsync(cancellationToken).ConfigureAwait(false);
        page.Request += mediator.OnRequest;
        page.RequestFailed += mediator.OnRequestFailed;
        await page.SetRequestInterceptionAsync(true).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CloseTargetAsync(ICDPSession session, string targetId)
    {
        try
        {
            await session.SendAsync("Target.closeTarget", new Dictionary<string, object> { ["targetId"] = targetId })
                .WaitAsync(CleanupTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Already closed together with the context.
        }
    }

    /// <summary>Lays out the header/footer bands at their final size and fails if their content does not fit.</summary>
    private static async Task MeasureBandsAsync(RenderEnvironment environment, IBrowserContext context, PreparedDocument document, string operationId, CancellationToken cancellationToken)
    {
        var url = new Uri($"http://op-{operationId}-bands{Security.LocalOrigins.DocumentHostSuffix}/bands.html");
        var measurementDocument = new PreparedDocument { DocumentUrl = url };
        measurementDocument.InlineDocuments[url.AbsoluteUri] = Encoding.UTF8.GetBytes(document.BandMeasurementHtml!);
        var page = await context.NewPageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mediator = new RequestMediator(environment, measurementDocument, operationId, measurementOnly: true, cancellationToken);
            await ConfigurePageAsync(page, mediator, allowScripts: false, CssMediaType.Print, cancellationToken).ConfigureAwait(false);
            await page.GoToAsync(url.AbsoluteUri, new NavigationOptions { WaitUntil = [WaitUntilNavigation.Load], Timeout = 0 })
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            await PollAsync(page, "document.fonts.status === 'loaded' && Array.from(document.images).every(i => i.complete)", cancellationToken).ConfigureAwait(false);
            if (mediator.Issues.FirstOrDefault(i => i.Required) is { } issue)
            {
                throw new PdfConversionException(PdfConversionError.InvalidRequest, $"Header/footer content is not self-contained: {issue.Resource} ({issue.Reason}).");
            }

            var overflow = await page.EvaluateExpressionAsync<string>(
                "['h2p-header','h2p-footer'].filter(id => { const e = document.getElementById(id); return e && (e.scrollHeight > e.clientHeight + 1 || e.scrollWidth > e.clientWidth + 1); }).join(',')")
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(overflow))
            {
                var which = overflow.Contains("header", StringComparison.Ordinal) ? "Header" : "Footer";
                throw new PdfConversionException(PdfConversionError.InvalidRequest,
                    $"{which} content does not fit in its band. Increase the {which.ToLowerInvariant()} Height (and the corresponding margin) or reduce its content.");
            }
        }
        finally
        {
            try
            {
                await page.CloseAsync().WaitAsync(CleanupTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The context is closed by the caller.
            }
        }
    }

    private static async Task WaitForReadinessAsync(IPage page, RequestMediator mediator, PdfReadinessOptions readiness, CancellationToken cancellationToken)
    {
        // Lazily loaded content would otherwise be missing from the printout.
        await page.EvaluateExpressionAsync(
            "document.querySelectorAll('img[loading=\"lazy\"],iframe[loading=\"lazy\"]').forEach(e => e.setAttribute('loading', 'eager')); true")
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        await PollAsync(page, "document.readyState === 'complete' && document.fonts.status === 'loaded' && Array.from(document.images).every(i => i.complete)", cancellationToken).ConfigureAwait(false);

        var idle = readiness.NetworkIdleTime ?? TimeSpan.Zero;
        while (mediator.InFlight > 0 || mediator.IdleFor < idle)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (readiness.WaitForSelector is { } selector)
        {
            while (true)
            {
                bool found;
                try
                {
                    found = await page.EvaluateFunctionAsync<bool>("s => !!document.querySelector(s)", selector).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (EvaluationFailedException)
                {
                    throw new PdfConversionException(PdfConversionError.InvalidRequest, "Readiness.WaitForSelector is not a valid CSS selector.");
                }

                if (found)
                {
                    break;
                }

                await Task.Delay(PollInterval * 4, cancellationToken).ConfigureAwait(false);
            }
        }

        if (readiness.WaitForExpression is { } expression)
        {
            while (true)
            {
                bool ready;
                try
                {
                    ready = await page.EvaluateFunctionAsync<bool>("async () => !!(await (" + expression + "\n))").WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (EvaluationFailedException ex) when (ex.Message.Contains("SyntaxError", StringComparison.Ordinal))
                {
                    throw new PdfConversionException(PdfConversionError.InvalidRequest, "Readiness.WaitForExpression is not a valid JavaScript expression.");
                }
                catch (EvaluationFailedException)
                {
                    ready = false;
                }

                if (ready)
                {
                    break;
                }

                await Task.Delay(PollInterval * 4, cancellationToken).ConfigureAwait(false);
            }

            // Content produced by the expression's completion may request more resources.
            while (mediator.InFlight > 0)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Navigations that never reach the network (for example to <c>about:blank</c>) cannot be intercepted. Rather than
    /// printing a replaced document, the conversion fails if the main frame no longer shows the document that was served.
    /// </summary>
    private static async Task EnsureOriginalDocumentAsync(IPage page, RequestMediator mediator, CancellationToken cancellationToken)
    {
        var location = await page.EvaluateExpressionAsync<string>("location.href.split('#')[0]").WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(location, mediator.MainDocumentUrl, StringComparison.Ordinal))
        {
            throw new PdfConversionException(PdfConversionError.RenderingFailed, "The document navigated away from itself; navigation is not permitted during conversion.");
        }
    }

    private static async Task PollAsync(IPage page, string condition, CancellationToken cancellationToken)
    {
        while (!await page.EvaluateExpressionAsync<bool>(condition).WaitAsync(cancellationToken).ConfigureAwait(false))
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ApplyResourcePolicy(RequestMediator mediator, ResourceFailurePolicy policy, List<ConversionWarning> warnings, ref int processed)
    {
        var issues = mediator.Issues.ToArray();
        for (; processed < issues.Length; processed++)
        {
            var issue = issues[processed];
            if (!issue.Required)
            {
                continue;
            }

            if (policy == ResourceFailurePolicy.Fail)
            {
                throw issue.Disposition == ResourceDisposition.Denied
                    ? new PdfConversionException(PdfConversionError.AccessDenied, $"A required resource was denied by policy: {issue.Resource} ({issue.Reason}).")
                    : new PdfConversionException(PdfConversionError.ResourceUnavailable, $"A required resource could not be loaded: {issue.Resource} ({issue.Reason}).");
            }

            warnings.Add(issue.Disposition == ResourceDisposition.Denied
                ? new ConversionWarning(ConversionWarningCode.ResourceDenied, $"Resource denied by policy and skipped: {issue.Reason}.", issue.Resource)
                : new ConversionWarning(ConversionWarningCode.ResourceUnavailable, $"Resource could not be loaded and was skipped: {issue.Reason}.", issue.Resource));
        }
    }

    private static void ThrowIfCrashed(bool crashed)
    {
        if (crashed)
        {
            throw new PdfConversionException(PdfConversionError.RenderingFailed, "The renderer process for this document crashed.");
        }
    }

    private static void ValidatePdf(byte[] pdf)
    {
        var header = "%PDF-"u8;
        var trailer = "%%EOF"u8;
        var tail = pdf.AsSpan(Math.Max(0, pdf.Length - 1024));
        if (pdf.Length < 64 || !pdf.AsSpan().StartsWith(header) || tail.IndexOf(trailer) < 0)
        {
            throw new PdfConversionException(PdfConversionError.RenderingFailed, "The renderer returned an incomplete PDF.");
        }
    }

    private static async Task CloseContextAsync(RenderEnvironment environment, BrowserInstance browser, IBrowserContext context)
    {
        try
        {
            await context.CloseAsync().WaitAsync(CleanupTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (browser.Browser.IsConnected)
            {
                // A context that cannot be closed may still hold state; never reuse that browser.
                environment.Host.MarkUnhealthy(browser, "a browser context could not be closed");
            }

            Log.FailureDetail(environment.Logger, "cleanup", ex);
        }
    }
}
