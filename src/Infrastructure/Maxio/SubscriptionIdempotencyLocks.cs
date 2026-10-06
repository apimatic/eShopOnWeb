using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Serializes subscribe operations per (user, plan) so a fast double-submit cannot race
/// past the "already subscribed?" check and create two live subscriptions. Held as a
/// singleton so the locks are stable across requests.
///
/// Note: an in-process guard is correct for the single PublicApi host. A multi-instance
/// deployment would use a distributed lock; Maxio is the system of record and the
/// check-then-create against it still prevents duplicates in that case for sequential
/// requests, so this primarily covers the concurrent double-click.
/// </summary>
public class SubscriptionIdempotencyLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new(StringComparer.Ordinal);

    public async Task<T> RunAsync<T>(string key, Func<Task<T>> action)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
