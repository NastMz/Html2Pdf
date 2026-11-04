namespace Nast.Html2Pdf.Concurrency;

/// <summary>
/// Bounded admission: at most <c>maxConcurrency</c> holders and <c>maxQueue</c> waiters. A slot is released exactly once
/// per lease, and closing the gate rejects new and waiting requests.
/// </summary>
internal sealed class AdmissionGate : IDisposable
{
    private readonly SemaphoreSlim _slots;
    private readonly int _maxQueue;
    private readonly CancellationTokenSource _closed = new();
    private int _queued;
    private int _active;

    public AdmissionGate(int maxConcurrency, int maxQueue)
    {
        if (maxConcurrency < 1 || maxConcurrency > 256)
        {
            throw new ArgumentException("Capacity.MaxConcurrentConversions must be between 1 and 256.");
        }

        if (maxQueue < 0 || maxQueue > 100_000)
        {
            throw new ArgumentException("Capacity.MaxQueueLength must be between 0 and 100000.");
        }

        MaxConcurrency = maxConcurrency;
        _maxQueue = maxQueue;
        _slots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public int MaxConcurrency { get; }

    public int MaxQueue => _maxQueue;

    public int Active => Volatile.Read(ref _active);

    public int Queued => Volatile.Read(ref _queued);

    public bool IsClosed => _closed.IsCancellationRequested;

    public async Task<Lease> EnterAsync(TimeSpan queueTimeout, CancellationToken cancellationToken)
    {
        if (IsClosed)
        {
            throw ShuttingDown();
        }

        if (_slots.Wait(0, CancellationToken.None))
        {
            return Admit();
        }

        if (Interlocked.Increment(ref _queued) > _maxQueue)
        {
            Interlocked.Decrement(ref _queued);
            throw new PdfConversionException(PdfConversionError.CapacityExceeded, "The conversion queue is full.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        bool entered;
        try
        {
            entered = await _slots.WaitAsync(queueTimeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw ShuttingDown();
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
        }

        if (!entered)
        {
            throw new PdfConversionException(PdfConversionError.CapacityExceeded, "No rendering capacity became available within the queue timeout.");
        }

        if (IsClosed)
        {
            _slots.Release();
            throw ShuttingDown();
        }

        return Admit();
    }

    private Lease Admit()
    {
        Interlocked.Increment(ref _active);
        return new Lease(this);
    }

    public void Close() => _closed.Cancel();

    private void Release()
    {
        Interlocked.Decrement(ref _active);
        _slots.Release();
    }

    private static PdfConversionException ShuttingDown() => new(PdfConversionError.ShuttingDown, "The converter is shutting down.");

    public void Dispose()
    {
        _closed.Dispose();
        _slots.Dispose();
    }

    /// <summary>Ownership of one rendering slot; releasing more than once has no effect.</summary>
    internal sealed class Lease(AdmissionGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
