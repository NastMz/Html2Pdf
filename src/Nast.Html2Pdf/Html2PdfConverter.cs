using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nast.Html2Pdf.Concurrency;
using Nast.Html2Pdf.Diagnostics;
using Nast.Html2Pdf.Output;
using Nast.Html2Pdf.Rendering;
using Nast.Html2Pdf.Security;
using Nast.Html2Pdf.Templating;
using PuppeteerSharp;

namespace Nast.Html2Pdf;

/// <summary>
/// Converts HTML, templates, local documents and authorized URLs to PDF using an owned headless Chromium process.
/// </summary>
/// <remarks>
/// <para>Create one instance per application (it is thread-safe) and dispose it on shutdown, or register it with
/// <c>services.AddHtml2Pdf()</c> so the container owns it. The browser starts on first use or on
/// <see cref="WarmUpAsync"/>.</para>
/// <para>Disposal calls <see cref="ShutdownAsync"/>: new work is rejected, queued work fails with
/// <see cref="PdfConversionError.ShuttingDown"/>, accepted work gets <see cref="Html2PdfOptions.ShutdownTimeout"/> to
/// finish, and then the browser process and its temporary profile are removed.</para>
/// </remarks>
public sealed class Html2PdfConverter : IHtml2PdfConverter, IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan CancellationSettleTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;
    private readonly AdmissionGate _gate;
    private readonly BrowserHost _host;
    private readonly RemoteFetcher _fetcher;
    private readonly RenderEnvironment _environment;
    private readonly DocumentPreparer _preparer;
    private readonly Html2PdfMetrics _metrics;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _operationsLock = new();
    private readonly TimeSpan _defaultTimeout;
    private readonly TimeSpan _queueTimeout;
    private readonly TimeSpan _shutdownTimeout;
    private readonly int _maxAttempts;
    private TaskCompletionSource? _operationsDrained;
    private int _operations;
    private Task? _shutdownTask;
    private volatile bool _stopped;
    private long _succeeded;
    private long _failed;

    /// <summary>Creates a converter. No browser is started until the first conversion or <see cref="WarmUpAsync"/>.</summary>
    /// <param name="options">Configuration; validated and copied, so later changes to the instance have no effect.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    public Html2PdfConverter(Html2PdfOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Browser, nameof(options.Browser));
        ArgumentNullException.ThrowIfNull(options.Capacity, nameof(options.Capacity));
        ArgumentNullException.ThrowIfNull(options.Network, nameof(options.Network));
        ArgumentNullException.ThrowIfNull(options.LocalFiles, nameof(options.LocalFiles));
        ArgumentNullException.ThrowIfNull(options.Templates, nameof(options.Templates));
        if (options.DefaultTimeout <= TimeSpan.Zero || options.DefaultTimeout > TimeSpan.FromHours(1))
        {
            throw new ArgumentException("DefaultTimeout must be positive and at most one hour.", nameof(options));
        }

        if (options.Capacity.QueueTimeout < TimeSpan.Zero || options.ShutdownTimeout < TimeSpan.Zero)
        {
            throw new ArgumentException("Capacity.QueueTimeout and ShutdownTimeout cannot be negative.", nameof(options));
        }

        if (options.MaxAttempts is < 1 or > 5)
        {
            throw new ArgumentException("MaxAttempts must be between 1 and 5.", nameof(options));
        }

        _logger = new SafeLogger((loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Nast.Html2Pdf"));
        _defaultTimeout = options.DefaultTimeout;
        _queueTimeout = options.Capacity.QueueTimeout;
        _shutdownTimeout = options.ShutdownTimeout;
        _maxAttempts = options.MaxAttempts;
        var network = new NetworkPolicy(options.Network);
        var files = new LocalFileGuard(options.LocalFiles);
        var templates = new TemplateRenderer(options.Templates);
        _gate = new AdmissionGate(options.Capacity.MaxConcurrentConversions, options.Capacity.MaxQueueLength);
        _metrics = new Html2PdfMetrics(() => _gate.Active, () => _gate.Queued, () => _host?.IsRunning == true ? 1 : 0);
        var browserOptions = new BrowserOptions
        {
            ExecutablePath = options.Browser.ExecutablePath,
            SearchStandardLocations = options.Browser.SearchStandardLocations,
            DisableSandbox = options.Browser.DisableSandbox,
            LaunchTimeout = options.Browser.LaunchTimeout,
            MaxConversionsPerBrowser = options.Browser.MaxConversionsPerBrowser,
            TemporaryDirectory = options.Browser.TemporaryDirectory,
        };
        _host = new BrowserHost(browserOptions, _logger, _metrics);
        _fetcher = new RemoteFetcher(network);
        _environment = new RenderEnvironment(_host, files, _fetcher, network, options.AllowScripts, _logger);
        _preparer = new DocumentPreparer(files, network, templates);
    }

    /// <summary>Fault-injection hook invoked immediately before a file is committed (tests only).</summary>
    internal Func<string, CancellationToken, Task>? BeforeFileCommit { get; set; }

    /// <summary>Fault-injection hook invoked after a slot is acquired, before preparation (tests only).</summary>
    internal Func<CancellationToken, Task>? AfterAdmission { get; set; }

    internal BrowserHost Host => _host;

    internal int OperationsInProgress => Volatile.Read(ref _operations);

    /// <inheritdoc />
    public Task<PdfDocument> ConvertAsync(PdfSource source, PdfConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return RunAsync(source, options, cancellationToken, (pdf, result, _) => Task.FromResult(
            new PdfDocument(pdf, result.OperationId, result.Timings, result.Warnings)));
    }

    /// <inheritdoc />
    public Task<PdfFileResult> ConvertToFileAsync(PdfSource source, string outputPath, bool overwrite = false, PdfConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(outputPath);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("The output path cannot be empty.", nameof(outputPath));
        }

        // Fail before rendering when the outcome is already known; the commit re-checks atomically.
        var fullPath = Path.GetFullPath(outputPath);
        return RunAsync(source, options, cancellationToken, async (pdf, result, token) =>
        {
            var committed = await AtomicFileWriter.WriteAsync(fullPath, pdf, overwrite, token, BeforeFileCommit).ConfigureAwait(false);
            return new PdfFileResult(committed, result);
        }, () =>
        {
            if (!overwrite && File.Exists(fullPath))
            {
                throw new PdfConversionException(PdfConversionError.OutputFailed, "The output file already exists and overwriting was not requested.");
            }

            if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            {
                throw new PdfConversionException(PdfConversionError.OutputFailed, "The output directory does not exist.");
            }
        });
    }

    /// <inheritdoc />
    public Task<PdfConversionResult> ConvertToStreamAsync(PdfSource source, Stream destination, PdfConversionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        }

        return RunAsync<PdfConversionResult>(source, options, cancellationToken, async (pdf, result, token) =>
        {
            try
            {
                await destination.WriteAsync(pdf, token).ConfigureAwait(false);
                await destination.FlushAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException or UnauthorizedAccessException)
            {
                throw new PdfConversionException(PdfConversionError.OutputFailed, $"Writing to the destination stream failed ({ex.GetType().Name}).");
            }

            return result;
        });
    }

    /// <summary>Starts the browser now instead of on the first conversion, surfacing provisioning problems early.</summary>
    /// <exception cref="PdfConversionException">The browser cannot be started (<see cref="PdfConversionError.RendererUnavailable"/>).</exception>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginOperation())
        {
            throw new PdfConversionException(PdfConversionError.ShuttingDown, "The converter is shutting down.");
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            await _host.WarmUpAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PdfConversionException(PdfConversionError.ShuttingDown, "The converter is shutting down.");
        }
        finally
        {
            EndOperation();
        }
    }

    /// <inheritdoc />
    public Html2PdfHealth GetHealth()
    {
        var status = _stopped ? Html2PdfStatus.Stopped
            : _gate.IsClosed ? Html2PdfStatus.ShuttingDown
            : _gate.MaxQueue > 0 && _gate.Queued >= _gate.MaxQueue ? Html2PdfStatus.Saturated
            : _host.IsRunning ? Html2PdfStatus.Ready
            : _host.LastFailureAt is not null ? Html2PdfStatus.Unavailable
            : Html2PdfStatus.Idle;
        return new Html2PdfHealth(status, _gate.Active, _gate.Queued, _gate.MaxConcurrency, _gate.MaxQueue,
            _host.IsRunning, _host.BrowserVersion, _host.LastFailureAt, Interlocked.Read(ref _succeeded), Interlocked.Read(ref _failed));
    }

    /// <summary>
    /// Stops accepting work, fails queued conversions with <see cref="PdfConversionError.ShuttingDown"/>, lets active
    /// conversions finish for up to <see cref="Html2PdfOptions.ShutdownTimeout"/>, cancels the rest, and releases the
    /// browser process and temporary files. Safe to call more than once and concurrently.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting; shutdown itself continues in the background.</param>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_operationsLock)
        {
            _shutdownTask ??= ShutdownCoreAsync();
        }

        return _shutdownTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

    private async Task ShutdownCoreAsync()
    {
        _gate.Close();
        Log.ShuttingDown(_logger, _gate.Active, _gate.Queued);
        if (!await WaitForOperationsAsync(_shutdownTimeout).ConfigureAwait(false))
        {
            Log.ShutdownCancelling(_logger, OperationsInProgress);
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
            await WaitForOperationsAsync(CancellationSettleTimeout).ConfigureAwait(false);
        }

        await _shutdownCts.CancelAsync().ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
        _fetcher.Dispose();
        _metrics.Dispose();
        _stopped = true;
    }

    private async Task<bool> WaitForOperationsAsync(TimeSpan timeout)
    {
        Task drained;
        lock (_operationsLock)
        {
            if (_operations == 0)
            {
                return true;
            }

            _operationsDrained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            drained = _operationsDrained.Task;
        }

        try
        {
            await drained.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool TryBeginOperation()
    {
        lock (_operationsLock)
        {
            if (_shutdownTask is not null)
            {
                return false;
            }

            _operations++;
            return true;
        }
    }

    private void EndOperation()
    {
        lock (_operationsLock)
        {
            if (--_operations == 0)
            {
                _operationsDrained?.TrySetResult();
            }
        }
    }

#pragma warning disable CA1068 // Internal helper: the output delegate reads better last.
    private async Task<T> RunAsync<T>(
        PdfSource source,
        PdfConversionOptions? options,
        CancellationToken cancellationToken,
        Func<byte[], PdfConversionResult, CancellationToken, Task<T>> output,
        Action? precheck = null)
#pragma warning restore CA1068
    {
        options ??= new PdfConversionOptions();
        var operationId = Guid.NewGuid().ToString("N")[..16];
        var started = Stopwatch.GetTimestamp();
        var stage = new StageTracker();
        TimeSpan queued = default, preparation = default, rendering = default, outputTime = default;
        var explicitReadiness = options.Readiness is { } r && (r.WaitForSelector is not null || r.WaitForExpression is not null || r.NetworkIdleTime is not null);
        using var activity = Html2PdfMetrics.StartActivity(operationId, source.Kind);

        if (!TryBeginOperation())
        {
            Interlocked.Increment(ref _failed);
            throw new PdfConversionException(PdfConversionError.ShuttingDown, "The converter is shutting down.", operationId);
        }

        var timeout = TimeSpan.Zero;
        try
        {
            var geometry = PageGeometry.Create(options);
            timeout = options.Timeout ?? _defaultTimeout;
            precheck?.Invoke();
            using var deadline = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _shutdownCts.Token);
            var token = linked.Token;

            byte[] pdf;
            var warnings = new List<ConversionWarning>();
            using (var slot = await EnterAsync(token).ConfigureAwait(false))
            {
                queued = Stopwatch.GetElapsedTime(started);
                _metrics.QueueWaited(queued);
                Log.Admitted(_logger, operationId, source.Kind, (long)queued.TotalMilliseconds);
                if (AfterAdmission is { } hook)
                {
                    await hook(token).ConfigureAwait(false);
                }

                stage.Set(RenderStage.Preparing);
                var mark = Stopwatch.GetTimestamp();
                var prepared = await _preparer.PrepareAsync(source, geometry, operationId, token).ConfigureAwait(false);
                preparation = Stopwatch.GetElapsedTime(mark);

                mark = Stopwatch.GetTimestamp();
                pdf = await RenderWithRetriesAsync(prepared, geometry, operationId, stage, warnings, token).ConfigureAwait(false);
                rendering = Stopwatch.GetElapsedTime(mark);
            }

            stage.Set(RenderStage.Output);
            var outputMark = Stopwatch.GetTimestamp();
            var interim = new PdfConversionResult(operationId, pdf.Length, Timings(), warnings.AsReadOnly());
            var result = await output(pdf, interim, token).ConfigureAwait(false);
            outputTime = Stopwatch.GetElapsedTime(outputMark);
            var final = Retime(result, Timings());

            Interlocked.Increment(ref _succeeded);
            _metrics.ConversionCompleted(source.Kind, "success", null, Stopwatch.GetElapsedTime(started));
            Log.Succeeded(_logger, operationId, source.Kind, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, pdf.Length, warnings.Count);
            return final;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            var total = Stopwatch.GetElapsedTime(started);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                _metrics.ConversionCompleted(source.Kind, "cancelled", null, total);
                Log.Cancelled(_logger, operationId, source.Kind, (long)total.TotalMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                throw new OperationCanceledException("The conversion was cancelled.", ex, cancellationToken);
            }

            var failure = Translate(ex, stage.Current, timeout, explicitReadiness).WithOperationId(operationId);
            if (failure.Error == PdfConversionError.CapacityExceeded)
            {
                _metrics.Rejected(stage.Current == RenderStage.Queued ? "queue" : "other");
            }

            _metrics.ConversionCompleted(source.Kind, "failure", failure.Error.ToString(), total);
            Log.Failed(_logger, operationId, source.Kind, (long)total.TotalMilliseconds, failure.Error, failure.Message);
            if (!ReferenceEquals(failure, ex))
            {
                Log.FailureDetail(_logger, operationId, ex);
            }

            activity?.SetStatus(ActivityStatusCode.Error, failure.Error.ToString());
            throw failure;
        }
        finally
        {
            EndOperation();
        }

        ConversionTimings Timings() => new(queued, preparation, rendering, outputTime, Stopwatch.GetElapsedTime(started));
    }

    private async Task<AdmissionGate.Lease> EnterAsync(CancellationToken token)
    {
        try
        {
            return await _gate.EnterAsync(_queueTimeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            throw new PdfConversionException(PdfConversionError.ShuttingDown, "The converter is shutting down.");
        }
    }

    private static T Retime<T>(T result, ConversionTimings timings)
    {
        object retimed = result switch
        {
            PdfDocument document => new PdfDocument(document.Content.ToArray(), document.OperationId, timings, document.Warnings),
            PdfFileResult file => new PdfFileResult(file.FilePath, new PdfConversionResult(file.OperationId, file.ByteCount, timings, file.Warnings)),
            PdfConversionResult plain => new PdfConversionResult(plain.OperationId, plain.ByteCount, timings, plain.Warnings),
            _ => result!,
        };
        return (T)retimed;
    }

    private async Task<byte[]> RenderWithRetriesAsync(PreparedDocument prepared, PageGeometry geometry, string operationId, StageTracker stage, List<ConversionWarning> warnings, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            stage.Set(RenderStage.Starting);
            using var lease = await _host.AcquireAsync(token).ConfigureAwait(false);
            var attemptWarnings = new List<ConversionWarning>();
            try
            {
                var pdf = await RenderSession.RunAsync(_environment, lease.Instance, prepared, geometry, operationId, stage, attemptWarnings, token).ConfigureAwait(false);
                warnings.AddRange(attemptWarnings);
                return pdf;
            }
            catch (Exception ex) when (!token.IsCancellationRequested && ClassifyFailure(ex, lease.Instance) is { } failure)
            {
                if (failure == InfrastructureFailure.Browser)
                {
                    _host.MarkUnhealthy(lease.Instance, "the browser failed during rendering");
                }

                if (attempt >= _maxAttempts)
                {
                    Log.FailureDetail(_logger, operationId, ex);
                    throw new PdfConversionException(PdfConversionError.RenderingFailed, "The browser failed while rendering the document and the retry limit was reached.");
                }

                Log.Retrying(_logger, operationId, ex.GetType().Name, attempt);
                Log.FailureDetail(_logger, operationId, ex);
                _metrics.Retried();
                warnings.Add(new ConversionWarning(ConversionWarningCode.Retried, "A renderer infrastructure failure occurred; the conversion was retried in a new browser context."));
            }
        }
    }

    private enum InfrastructureFailure
    {
        /// <summary>The browser process or its connection failed; the browser is replaced.</summary>
        Browser,

        /// <summary>A DevTools session or execution context vanished mid-command (a known protocol race); the browser is healthy.</summary>
        Session,
    }

    private static readonly string[] TransientProtocolMessages =
    [
        "Target closed", "Session closed", "Connection closed", "Session with given id not found", "No target with given id",
        "Execution context was destroyed", "Cannot find context with specified id", "Inspected target navigated or closed",
    ];

    /// <summary>
    /// Only infrastructure failures are retried. Failures attributable to the request (policy, missing resources,
    /// templates, layout, deadlines) are <see cref="PdfConversionException"/>s and never retried.
    /// </summary>
    private static InfrastructureFailure? ClassifyFailure(Exception ex, BrowserInstance instance)
    {
        if (instance.Disconnected || !instance.Browser.IsConnected || ex is ProcessException)
        {
            return InfrastructureFailure.Browser;
        }

        if (ex is PdfConversionException)
        {
            return null;
        }

        return ex is TargetClosedException
               || (ex is PuppeteerException && TransientProtocolMessages.Any(m => ex.Message.Contains(m, StringComparison.OrdinalIgnoreCase)))
            ? InfrastructureFailure.Session
            : null;
    }

    private PdfConversionException Translate(Exception ex, RenderStage stage, TimeSpan timeout, bool explicitReadiness)
    {
        switch (ex)
        {
            case PdfConversionException conversion:
                return conversion;
            case OperationCanceledException when _shutdownCts.IsCancellationRequested:
                return new PdfConversionException(PdfConversionError.ShuttingDown, "The converter shut down before the conversion completed.");
            case OperationCanceledException or TimeoutException:
                if (stage == RenderStage.Readiness && explicitReadiness)
                {
                    return new PdfConversionException(PdfConversionError.ReadinessNotMet,
                        $"The configured readiness conditions were not met within the deadline ({timeout.TotalSeconds:0.###} s).");
                }

                return new PdfConversionException(PdfConversionError.Timeout,
                    $"The conversion deadline ({timeout.TotalSeconds:0.###} s) elapsed during the {StageName(stage)} stage.");
            case ArgumentException or FormatException:
                return new PdfConversionException(PdfConversionError.InvalidRequest, "The request is invalid: " + ex.Message);
            default:
                return new PdfConversionException(PdfConversionError.RenderingFailed, $"The renderer failed ({ex.GetType().Name}).");
        }
    }

    private static string StageName(RenderStage stage) => stage switch
    {
        RenderStage.Queued => "queue",
        RenderStage.Preparing => "preparation",
        RenderStage.Starting => "browser start",
        RenderStage.Loading => "document loading",
        RenderStage.Readiness => "readiness",
        RenderStage.Printing => "printing",
        _ => "output",
    };
}
