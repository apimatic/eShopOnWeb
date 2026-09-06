using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caps how many Maxio calls this process has in flight at once.
/// Maxio limits a subdomain to a handful of concurrent workers and queues anything beyond that,
/// so throttling here keeps requests fast instead of piling them up on the far side.
/// Registered as a singleton so the limit is process-wide.
/// </summary>
public sealed class MaxioRequestGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public MaxioRequestGate(IOptions<MaxioOptions> options)
    {
        var maxConcurrency = Math.Max(1, options.Value.MaxConcurrentRequests);
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
