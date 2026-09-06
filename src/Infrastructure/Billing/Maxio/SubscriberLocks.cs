using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Serialises enrollment per shopper inside this process, so the two halves of a double-click cannot
/// interleave between "does this shopper already have a subscription?" and "create one".
/// <para>
/// This is a latency optimisation, not the correctness guarantee: it does nothing across processes.
/// Correctness comes from the deterministic references that Maxio enforces as unique, which
/// <see cref="MaxioSubscriptionBillingService"/> relies on regardless of whether this lock was taken.
/// </para>
/// </summary>
public class SubscriberLocks
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(AcquireTimeout, cancellationToken))
        {
            throw new TimeoutException($"Timed out waiting to process a billing operation for '{key}'.");
        }

        return new Release(semaphore);
    }

    private sealed class Release : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Release(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
