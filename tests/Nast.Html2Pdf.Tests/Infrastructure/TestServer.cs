using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Nast.Html2Pdf.Tests.Infrastructure;

/// <summary>A loopback HTTP server that records every request it receives.</summary>
internal sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, Func<HttpListenerContext, Task>> _routes = new(StringComparer.Ordinal);
    private int _concurrent;
    private int _maxConcurrent;

    public TestServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public int Hits => Requests.Count;

    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    public void Map(string path, Func<HttpListenerContext, Task> handler) => _routes[path] = handler;

    public void MapText(string path, string contentType, string body, int status = 200, IDictionary<string, string>? headers = null)
        => MapBytes(path, contentType, Encoding.UTF8.GetBytes(body), status, headers);

    public void MapBytes(string path, string contentType, byte[] body, int status = 200, IDictionary<string, string>? headers = null)
        => Map(path, async context =>
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                context.Response.Headers[name] = value;
            }

            await context.Response.OutputStream.WriteAsync(body);
        });

    public void MapRedirect(string path, string location, int status = 302)
        => Map(path, context =>
        {
            context.Response.StatusCode = status;
            context.Response.RedirectLocation = location;
            return Task.CompletedTask;
        });

    public void MapDelayed(string path, TimeSpan delay, string contentType = "image/svg+xml", string body = Svg.BlueSquare)
        => Map(path, async context =>
        {
            await Task.Delay(delay);
            context.Response.ContentType = contentType;
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
        });

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var current = Interlocked.Increment(ref _concurrent);
        InterlockedMax(ref _maxConcurrent, current);
        try
        {
            var headers = context.Request.Headers.AllKeys.Where(k => k is not null)
                .ToDictionary(k => k!, k => context.Request.Headers[k]!, StringComparer.OrdinalIgnoreCase);
            Requests.Enqueue(new RecordedRequest(context.Request.HttpMethod, context.Request.Url!.PathAndQuery, headers));
            if (_routes.TryGetValue(context.Request.Url.AbsolutePath, out var handler))
            {
                await handler(context);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            Interlocked.Decrement(ref _concurrent);
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    public static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}

internal sealed record RecordedRequest(string Method, string PathAndQuery, IReadOnlyDictionary<string, string> Headers);

/// <summary>A raw TCP listener that counts connection attempts (for protocols that are not HTTP requests).</summary>
internal sealed class ConnectionCanary : IDisposable
{
    private readonly TcpListener _listener;
    private int _connections;

    public ConnectionCanary()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
            }
        });
    }

    public int Port { get; }

    public int Connections => Volatile.Read(ref _connections);

    public void Dispose() => _listener.Stop();
}

internal static class Svg
{
    public const string BlueSquare = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"40\"><rect width=\"40\" height=\"40\" fill=\"#0000ff\"/></svg>";
}
