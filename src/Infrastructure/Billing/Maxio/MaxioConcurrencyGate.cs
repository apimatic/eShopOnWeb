using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Caps how many requests this process has in flight to Maxio at once.
/// </summary>
/// <remarks>
/// Maxio limits a subdomain to a small number of concurrent API workers and queues anything over
/// that, so firing more requests in parallel makes every one of them slower rather than faster.
/// This is a singleton because <see cref="MaxioResilienceHandler"/> instances are pooled and
/// recycled by <c>IHttpClientFactory</c>; the budget has to outlive them.
/// </remarks>
public sealed class MaxioConcurrencyGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public MaxioConcurrencyGate(IOptions<MaxioOptions> options)
    {
        var permits = Math.Max(1, options.Value.MaxConcurrentRequests);
        _semaphore = new SemaphoreSlim(permits, permits);
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
