using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Process-wide cap on in-flight Maxio calls. Maxio limits each site by concurrency rather than by
/// request rate, so queuing locally is strictly better than being throttled remotely.
/// </summary>
public sealed class MaxioConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public MaxioConcurrencyLimiter(int maxConcurrentRequests)
    {
        _semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentRequests));
    }

    public SemaphoreSlim Semaphore => _semaphore;

    public void Dispose() => _semaphore.Dispose();
}
