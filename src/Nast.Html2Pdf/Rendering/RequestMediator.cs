using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Nast.Html2Pdf.Diagnostics;
using Nast.Html2Pdf.Security;
using PuppeteerSharp;

namespace Nast.Html2Pdf.Rendering;

internal enum ResourceDisposition
{
    Denied,
    Unavailable,
}

internal sealed record ResourceIssue(ResourceDisposition Disposition, string Resource, string Reason, bool Required);

/// <summary>
/// Answers every request a page makes. In-memory documents and authorized local files are served by the library,
/// remote requests are performed by <see cref="RemoteFetcher"/> under the network policy, and everything else is
/// refused. Chromium never touches the network or the file system itself.
/// </summary>
internal sealed class RequestMediator
{
    private const string ScriptBlockingPolicy = "script-src 'none'; object-src 'none'; worker-src 'none'; frame-src 'self' data: http: https:";

    private static readonly HashSet<ResourceType> OptionalTypes =
    [
        ResourceType.Other, ResourceType.Ping, ResourceType.Beacon, ResourceType.Manifest, ResourceType.WebSocket, ResourceType.Unknown,
    ];

    private readonly RenderEnvironment _environment;
    private readonly PreparedDocument _document;
    private readonly string _operationId;
    private readonly bool _measurementOnly;
    private readonly CancellationToken _cancellationToken;
    private readonly ConcurrentQueue<ResourceIssue> _issues = new();
    private int _inFlight;
    private long _lastActivity = Stopwatch.GetTimestamp();
    private readonly ConcurrentDictionary<string, byte> _handledFailures = new(StringComparer.Ordinal);
    private volatile bool _navigationLocked;

    public RequestMediator(RenderEnvironment environment, PreparedDocument document, string operationId, bool measurementOnly, CancellationToken cancellationToken)
    {
        _environment = environment;
        _document = document;
        _operationId = operationId;
        _measurementOnly = measurementOnly;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Failure of the main document, if any.</summary>
    public PdfConversionException? MainFailure { get; private set; }

    public IReadOnlyCollection<ResourceIssue> Issues => _issues;

    public int InFlight => Volatile.Read(ref _inFlight);

    public TimeSpan IdleFor => InFlight > 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastActivity));

    /// <summary>After the initial load, the main frame may not navigate elsewhere.</summary>
    public void LockNavigation() => _navigationLocked = true;

    public bool NavigationLocked => _navigationLocked;

    public void OnRequest(object? sender, RequestEventArgs e)
    {
        Interlocked.Increment(ref _inFlight);
        Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());
        _ = HandleAsync(e.Request);
    }

    private async Task HandleAsync(IRequest request)
    {
        try
        {
            await DispatchAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The page or context may already be closed; there is nobody left to answer.
            Log.FailureDetail(_environment.Logger, _operationId, ex);
        }
        finally
        {
            Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task DispatchAsync(IRequest request)
    {
        var url = request.Url;
        var isMainNavigation = request.IsNavigationRequest && request.Frame?.ParentFrame is null;
        var required = !OptionalTypes.Contains(request.ResourceType);
        if (isMainNavigation && _navigationLocked)
        {
            // Once the document has been answered, the main frame may not be replaced (meta refresh, location changes,
            // form submissions). Aborting would commit an error page; 204 No Content keeps the current document.
            _handledFailures.TryAdd(request.Id, 0);
            await request.RespondAsync(new ResponseData { Status = HttpStatusCode.NoContent, BodyData = [] }).ConfigureAwait(false);
            return;
        }

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            // Self-contained; no network or file access involved.
            LockIfMainDocument(request, isMainNavigation, 200);
            await request.ContinueAsync().ConfigureAwait(false);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await DenyAsync(request, isMainNavigation, required, url, "invalid URL").ConfigureAwait(false);
            return;
        }

        if (_document.InlineDocuments.TryGetValue(uri.AbsoluteUri, out var inline))
        {
            await RespondAsync(request, 200, "text/html; charset=utf-8", inline, null).ConfigureAwait(false);
            return;
        }

        if (_measurementOnly)
        {
            await DenyAsync(request, isMainNavigation, required, url, "header and footer content must not reference external resources; embed images as data: URIs").ConfigureAwait(false);
            return;
        }

        if (LocalOrigins.TryGetRootIndex(uri, out _))
        {
            await ServeLocalAsync(request, uri, isMainNavigation, required).ConfigureAwait(false);
            return;
        }

        if (LocalOrigins.IsLocal(uri))
        {
            await NotFoundAsync(request, isMainNavigation, required, url, "not found").ConfigureAwait(false);
            return;
        }

        if (uri.Scheme is "http" or "https")
        {
            await ServeRemoteAsync(request, uri, isMainNavigation, required).ConfigureAwait(false);
            return;
        }

        await DenyAsync(request, isMainNavigation, required, url, $"scheme '{uri.Scheme}' is not allowed").ConfigureAwait(false);
    }

    private async Task ServeLocalAsync(IRequest request, Uri uri, bool isMainNavigation, bool required)
    {
        if (!_document.LocalAccess)
        {
            await DenyAsync(request, isMainNavigation, required, uri.AbsoluteUri, "local file access is not granted to this conversion").ConfigureAwait(false);
            return;
        }

        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            await DenyAsync(request, isMainNavigation, required, uri.AbsoluteUri, "method not allowed").ConfigureAwait(false);
            return;
        }

        var result = _environment.Files.Read(uri);
        switch (result.Status)
        {
            case LocalReadStatus.Success:
                await RespondAsync(request, 200, result.ContentType!, request.Method == HttpMethod.Head ? [] : result.Content!, null).ConfigureAwait(false);
                break;
            case LocalReadStatus.Denied:
                await DenyAsync(request, isMainNavigation, required, uri.AbsoluteUri, "path is outside the allowed local roots").ConfigureAwait(false);
                break;
            case LocalReadStatus.TooLarge:
                await NotFoundAsync(request, isMainNavigation, required, uri.AbsoluteUri, "file exceeds LocalFiles.MaxFileBytes").ConfigureAwait(false);
                break;
            default:
                await NotFoundAsync(request, isMainNavigation, required, uri.AbsoluteUri, "file not found").ConfigureAwait(false);
                break;
        }
    }

    private async Task ServeRemoteAsync(IRequest request, Uri uri, bool isMainNavigation, bool required)
    {
        if (!_environment.Network.AnyHostAllowed)
        {
            await DenyAsync(request, isMainNavigation, required, uri.AbsoluteUri, "network access is disabled").ConfigureAwait(false);
            return;
        }

        var result = await _environment.Fetcher.FetchAsync(uri, request.Method.Method, request.Headers ?? new Dictionary<string, string>(), _cancellationToken).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case FetchOutcome.Denied:
                await DenyAsync(request, isMainNavigation, required, uri.AbsoluteUri, result.Reason!).ConfigureAwait(false);
                return;
            case FetchOutcome.Failed:
                await FailAsync(request, isMainNavigation, required, uri.AbsoluteUri, result.Reason!).ConfigureAwait(false);
                return;
        }

        var headers = result.Headers!;
        if (result.Status is >= 300 and < 400 && headers.Get("location") is { } location)
        {
            await RespondAsync(request, result.Status, null, [], new Dictionary<string, object> { ["Location"] = location }).ConfigureAwait(false);
            return;
        }

        if (result.Status >= 400)
        {
            Record(isMainNavigation, ResourceDisposition.Unavailable, required, uri.AbsoluteUri, $"HTTP {result.Status}");
        }

        var extra = new Dictionary<string, object>();
        foreach (var (name, value) in (IReadOnlyDictionary<string, string>)headers)
        {
            if (name is not ("content-type" or "location"))
            {
                extra[name] = value;
            }
        }

        await RespondAsync(request, result.Status, headers.Get("content-type"), result.Body!, extra).ConfigureAwait(false);
    }

    private void LockIfMainDocument(IRequest request, bool isMainNavigation, int status)
    {
        // Redirect hops of the initial navigation are allowed; anything after the final response is not.
        if (isMainNavigation && !_navigationLocked && status is not (>= 300 and < 400))
        {
            MainDocumentUrl = request.Url.Split('#')[0];
            MainDocumentStatus = status;
            _navigationLocked = true;
        }
    }

    /// <summary>URL of the document that was finally served to the main frame.</summary>
    public string? MainDocumentUrl { get; private set; }

    /// <summary>HTTP status of the document that was finally served to the main frame.</summary>
    public int? MainDocumentStatus { get; private set; }

    /// <summary>
    /// Records required requests that failed inside the browser after (or without) mediation, for example a redirect
    /// to a forbidden scheme or a CORS failure, so a conversion never succeeds with a silently missing resource.
    /// </summary>
    public void OnRequestFailed(object? sender, RequestEventArgs e)
    {
        var request = e.Request;
        if (_handledFailures.ContainsKey(request.Id) || OptionalTypes.Contains(request.ResourceType)
            || (!_environment.AllowScripts && request.ResourceType == ResourceType.Script))
        {
            return;
        }

        var isMainNavigation = request.IsNavigationRequest && request.Frame?.ParentFrame is null;
        if (isMainNavigation && _navigationLocked)
        {
            return;
        }

        Record(isMainNavigation, ResourceDisposition.Unavailable, true, request.Url, $"load failed: {request.FailureText ?? "unknown error"}");
    }

    private async Task RespondAsync(IRequest request, int status, string? contentType, byte[] body, Dictionary<string, object>? headers)
    {
        LockIfMainDocument(request, request.IsNavigationRequest && request.Frame?.ParentFrame is null, status);
        headers ??= [];
        if (!_environment.AllowScripts)
        {
            // Defense in depth: scripts are also disabled through the DevTools protocol, which does not reach every frame type.
            headers["Content-Security-Policy"] = ScriptBlockingPolicy;
        }

        headers["Cache-Control"] = "no-store";
        await request.RespondAsync(new ResponseData
        {
            Status = (HttpStatusCode)status,
            ContentType = contentType,
            BodyData = body,
            Headers = headers,
        }).ConfigureAwait(false);
    }

    private async Task DenyAsync(IRequest request, bool isMainNavigation, bool required, string url, string reason)
    {
        _handledFailures.TryAdd(request.Id, 0);
        Record(isMainNavigation, ResourceDisposition.Denied, required, url, reason);
        await request.AbortAsync(RequestAbortErrorCode.BlockedByClient).ConfigureAwait(false);
    }

    private async Task FailAsync(IRequest request, bool isMainNavigation, bool required, string url, string reason)
    {
        _handledFailures.TryAdd(request.Id, 0);
        Record(isMainNavigation, ResourceDisposition.Unavailable, required, url, reason);
        await request.AbortAsync(RequestAbortErrorCode.Failed).ConfigureAwait(false);
    }

    private async Task NotFoundAsync(IRequest request, bool isMainNavigation, bool required, string url, string reason)
    {
        Record(isMainNavigation, ResourceDisposition.Unavailable, required, url, reason);
        await RespondAsync(request, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"), null).ConfigureAwait(false);
    }

    private void Record(bool isMainNavigation, ResourceDisposition disposition, bool required, string url, string reason)
    {
        var resource = Redaction.Url(url);
        Log.ResourceIssue(_environment.Logger, _operationId, disposition == ResourceDisposition.Denied ? "denied" : "unavailable", resource, reason);
        if (isMainNavigation && !_navigationLocked)
        {
            MainFailure ??= disposition == ResourceDisposition.Denied
                ? new PdfConversionException(PdfConversionError.AccessDenied, $"The document was denied by policy: {resource} ({reason}).")
                : new PdfConversionException(PdfConversionError.ResourceUnavailable, $"The document could not be loaded: {resource} ({reason}).");
            return;
        }

        _issues.Enqueue(new ResourceIssue(disposition, resource, reason, required));
    }
}

/// <summary>Services shared by all conversions of a converter.</summary>
internal sealed record RenderEnvironment(
    BrowserHost Host,
    LocalFileGuard Files,
    RemoteFetcher Fetcher,
    NetworkPolicy Network,
    bool AllowScripts,
    ILogger Logger);

/// <summary>What a rendering attempt loads.</summary>
internal sealed class PreparedDocument
{
    public required Uri DocumentUrl { get; init; }

    /// <summary>Documents served from memory, by absolute URL.</summary>
    public Dictionary<string, byte[]> InlineDocuments { get; } = new(StringComparer.Ordinal);

    public bool LocalAccess { get; init; }

    public string? HeaderHtml { get; init; }

    public string? FooterHtml { get; init; }

    public string? BandMeasurementHtml { get; init; }
}
