using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caps the number of Maxio calls this process has in flight at once.
/// </summary>
/// <remarks>
/// Maxio limits a site to four concurrent API calls and queues anything above that, so exceeding
/// the ceiling buys no throughput — it only lengthens every caller's latency and invites
/// throttling. Registered as a singleton so the ceiling is per process, not per typed-client
/// instance.
/// </remarks>
public sealed class MaxioRequestGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public MaxioRequestGate(int maxConcurrentRequests)
    {
        if (maxConcurrentRequests < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequests));
        }

        _semaphore = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Ticket(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Ticket : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Ticket(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
