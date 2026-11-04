using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace Nast.Html2Pdf.Security;

internal enum FetchOutcome
{
    Success,
    Denied,
    Failed,
}

internal sealed record FetchResult(FetchOutcome Outcome, int Status = 0, byte[]? Body = null, IReadOnlyDictionary<string, string>? Headers = null, string? Reason = null);

/// <summary>
/// Performs remote requests on behalf of documents. Destinations are re-validated after DNS resolution and the
/// connection is made to the validated address, so DNS rebinding cannot redirect a request to a forbidden address.
/// No cookies, credentials, proxies or automatic redirects are used; Chromium follows redirects through the
/// interception layer so every hop is checked again.
/// </summary>
internal sealed class RemoteFetcher : IDisposable
{
    private static readonly string[] ForwardedRequestHeaders = ["accept", "accept-language", "user-agent", "origin"];

    private static readonly string[] ForwardedResponseHeaders =
    [
        "content-type", "content-language", "location",
        "access-control-allow-origin", "access-control-allow-headers", "access-control-allow-methods", "access-control-expose-headers",
    ];

    private readonly NetworkPolicy _policy;
    private readonly HttpClient _client;

    public RemoteFetcher(NetworkPolicy policy)
    {
        _policy = policy;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            ConnectCallback = ConnectAsync,
        };
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<FetchResult> FetchAsync(Uri uri, string method, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        var denial = _policy.Deny(uri);
        if (denial is not null)
        {
            return new FetchResult(FetchOutcome.Denied, Reason: denial);
        }

        if (method is not ("GET" or "HEAD"))
        {
            return new FetchResult(FetchOutcome.Denied, Reason: $"method {method} is not allowed");
        }

        using var request = new HttpRequestMessage(method == "HEAD" ? HttpMethod.Head : HttpMethod.Get, uri);
        foreach (var (name, value) in headers)
        {
            if (ForwardedRequestHeaders.Contains(name.ToLowerInvariant()))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var responseHeaders = CollectHeaders(response, uri);
            if (response.Content.Headers.ContentLength > _policy.MaxResponseBytes)
            {
                return new FetchResult(FetchOutcome.Failed, Reason: "response exceeds the configured size limit");
            }

            var body = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                return new FetchResult(FetchOutcome.Failed, Reason: "response exceeds the configured size limit");
            }

            return new FetchResult(FetchOutcome.Success, (int)response.StatusCode, body, responseHeaders);
        }
        catch (HttpRequestException ex) when (FindDenial(ex) is { } denied)
        {
            return new FetchResult(FetchOutcome.Denied, Reason: denied.Message);
        }
        catch (HttpRequestException ex)
        {
            return new FetchResult(FetchOutcome.Failed, Reason: DescribeFailure(ex));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchResult(FetchOutcome.Failed, Reason: "connection timed out");
        }
    }

    private async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _policy.MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response, Uri requestUri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            var name = header.Key.ToLowerInvariant();
            if (!ForwardedResponseHeaders.Contains(name))
            {
                continue;
            }

            var value = string.Join(", ", header.Value);
            if (name == "location")
            {
                // Resolve relative redirects so Chromium re-requests an absolute URL that is validated again.
                if (!Uri.TryCreate(requestUri, value, out var target))
                {
                    continue;
                }

                value = target.AbsoluteUri;
            }

            result[name] = value;
        }

        return result;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                throw new HttpRequestException("host name could not be resolved");
            }
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException("host name could not be resolved");
        }

        // Refuse the destination if any resolved address is forbidden: mixed answers are a rebinding signal.
        if (addresses.Any(a => !_policy.Addresses.IsAllowed(a)))
        {
            throw new DestinationDeniedException("host resolves to an address that is not a permitted destination");
        }

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                last = ex;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("connection failed", last);
    }

    private static DestinationDeniedException? FindDenial(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is DestinationDeniedException denied)
            {
                return denied;
            }
        }

        return null;
    }

    private static string DescribeFailure(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "host name could not be resolved",
        HttpRequestError.ConnectionError => "connection failed",
        HttpRequestError.SecureConnectionError => "TLS negotiation or certificate validation failed",
        HttpRequestError.ResponseEnded or HttpRequestError.InvalidResponse => "invalid response",
        _ => ex.Message == "host name could not be resolved" ? ex.Message : "request failed",
    };

    public void Dispose() => _client.Dispose();

    private sealed class DestinationDeniedException(string message) : Exception(message);
}

internal static class HeaderExtensions
{
    public static string? Get(this IReadOnlyDictionary<string, string> headers, string name)
        => headers.TryGetValue(name, out var value) ? value : null;
}
