using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Caps how many Maxio API calls this process has in flight at once.
/// </summary>
/// <remarks>
/// Maxio limits by concurrency rather than by requests per second: a site allows a small number of
/// concurrent API calls and queues, slows or rejects the excess. Holding the line here means bursts
/// wait locally for a slot instead of arriving as HTTP 429s and dragging every other call down with
/// them. Registered as a singleton so the budget is shared across the whole process.
/// </remarks>
public sealed class MaxioRequestThrottle : IDisposable
{
    /// <summary>Concurrent calls a site permits before requests start being queued server side.</summary>
    public const int DefaultMaxConcurrentRequests = 4;

    private readonly SemaphoreSlim _slots;

    public MaxioRequestThrottle(int maxConcurrentRequests = DefaultMaxConcurrentRequests)
    {
        if (maxConcurrentRequests < 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests));

        _slots = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        return new Slot(_slots);
    }

    public void Dispose() => _slots.Dispose();

    private sealed class Slot : IDisposable
    {
        private SemaphoreSlim? _slots;

        public Slot(SemaphoreSlim slots) => _slots = slots;

        public void Dispose()
        {
            var slots = Interlocked.Exchange(ref _slots, null);
            slots?.Release();
        }
    }
}
