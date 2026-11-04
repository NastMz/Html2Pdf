namespace Nast.Html2Pdf;

/// <summary>Overall state of a converter.</summary>
public enum Html2PdfStatus
{
    /// <summary>Accepting work; the browser starts on first use (or with <see cref="Html2PdfConverter.WarmUpAsync"/>).</summary>
    Idle,
    /// <summary>Accepting work with a running browser.</summary>
    Ready,
    /// <summary>The queue is full; new requests are rejected until capacity frees up.</summary>
    Saturated,
    /// <summary>The most recent browser start failed and no browser is running. Requests will retry the start.</summary>
    Unavailable,
    /// <summary>Shutting down: no new work is accepted.</summary>
    ShuttingDown,
    /// <summary>Shut down; all owned resources have been released.</summary>
    Stopped,
}

/// <summary>A point-in-time snapshot for readiness probes and dashboards.</summary>
/// <param name="Status">Overall state.</param>
/// <param name="ActiveConversions">Conversions holding a rendering slot.</param>
/// <param name="QueuedConversions">Conversions waiting for a slot.</param>
/// <param name="MaxConcurrentConversions">Configured slot count.</param>
/// <param name="MaxQueueLength">Configured queue length.</param>
/// <param name="BrowserRunning">Whether a browser process is running.</param>
/// <param name="BrowserVersion">Version reported by the last started browser.</param>
/// <param name="LastBrowserFailure">Time of the last browser start failure or unexpected exit.</param>
/// <param name="SucceededConversions">Conversions that succeeded since creation.</param>
/// <param name="FailedConversions">Conversions that failed (including cancellations) since creation.</param>
public sealed record Html2PdfHealth(
    Html2PdfStatus Status,
    int ActiveConversions,
    int QueuedConversions,
    int MaxConcurrentConversions,
    int MaxQueueLength,
    bool BrowserRunning,
    string? BrowserVersion,
    DateTimeOffset? LastBrowserFailure,
    long SucceededConversions,
    long FailedConversions)
{
    /// <summary>Whether new conversions are currently accepted.</summary>
    public bool IsAcceptingWork => Status is Html2PdfStatus.Idle or Html2PdfStatus.Ready or Html2PdfStatus.Unavailable;
}
