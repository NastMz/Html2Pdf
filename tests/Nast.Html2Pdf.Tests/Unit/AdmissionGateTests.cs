using Nast.Html2Pdf.Concurrency;

namespace Nast.Html2Pdf.Tests.Unit;

public sealed class AdmissionGateTests
{
    [Fact]
    public async Task Never_admits_more_than_the_limit_and_never_loses_slots()
    {
        using var gate = new AdmissionGate(maxConcurrency: 3, maxQueue: 1000);
        var active = 0;
        var peak = 0;
        var rejected = 0;
        var tasks = Enumerable.Range(0, 400).Select(async i =>
        {
            try
            {
                using var lease = await gate.EnterAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                var now = Interlocked.Increment(ref active);
                InterlockedMax(ref peak, now);
                await Task.Delay(Random.Shared.Next(0, 3));

                // Stop counting this task as a holder before its slot can be handed to another task.
                Interlocked.Decrement(ref active);
                if (i % 7 == 0)
                {
                    lease.Dispose(); // an explicit release followed by the using-release must release only once
                }
            }
            catch (PdfConversionException)
            {
                Interlocked.Increment(ref rejected);
            }
        });
        await Task.WhenAll(tasks);

        Assert.True(peak <= 3, $"peak {peak}");
        Assert.Equal(0, rejected);
        Assert.Equal(0, gate.Active);
        Assert.Equal(0, gate.Queued);

        // All slots are still available afterwards.
        var leases = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => gate.EnterAsync(TimeSpan.Zero, CancellationToken.None)));
        await Assert.ThrowsAsync<PdfConversionException>(() => gate.EnterAsync(TimeSpan.Zero, CancellationToken.None));
        foreach (var lease in leases)
        {
            lease.Dispose();
        }
    }

    [Fact]
    public async Task Rejects_when_the_queue_is_full_and_when_waiting_times_out()
    {
        using var gate = new AdmissionGate(1, 1);
        using var held = await gate.EnterAsync(TimeSpan.Zero, CancellationToken.None);
        var queued = gate.EnterAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);
        var full = await Assert.ThrowsAsync<PdfConversionException>(() => gate.EnterAsync(TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.Equal(PdfConversionError.CapacityExceeded, full.Error);
        var expired = await Assert.ThrowsAsync<PdfConversionException>(() => queued);
        Assert.Equal(PdfConversionError.CapacityExceeded, expired.Error);
        Assert.Equal(0, gate.Queued);
        Assert.Equal(1, gate.Active);
    }

    [Fact]
    public async Task Cancellation_while_queued_releases_the_queue_position()
    {
        using var gate = new AdmissionGate(1, 1);
        using var held = await gate.EnterAsync(TimeSpan.Zero, CancellationToken.None);
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnterAsync(TimeSpan.FromSeconds(10), cts.Token));
        Assert.Equal(0, gate.Queued);
        var next = gate.EnterAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        held.Dispose();
        (await next).Dispose();
        Assert.Equal(0, gate.Active);
    }

    [Fact]
    public async Task Closing_rejects_waiters_and_new_requests()
    {
        using var gate = new AdmissionGate(1, 5);
        var held = await gate.EnterAsync(TimeSpan.Zero, CancellationToken.None);
        var waiter = gate.EnterAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        gate.Close();
        Assert.Equal(PdfConversionError.ShuttingDown, (await Assert.ThrowsAsync<PdfConversionException>(() => waiter)).Error);
        Assert.Equal(PdfConversionError.ShuttingDown, (await Assert.ThrowsAsync<PdfConversionException>(() => gate.EnterAsync(TimeSpan.Zero, CancellationToken.None))).Error);
        held.Dispose();
        Assert.Equal(0, gate.Active);
    }

    [Fact]
    public void Limits_are_validated()
    {
        Assert.Throws<ArgumentException>(() => new AdmissionGate(0, 1));
        Assert.Throws<ArgumentException>(() => new AdmissionGate(1, -1));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
