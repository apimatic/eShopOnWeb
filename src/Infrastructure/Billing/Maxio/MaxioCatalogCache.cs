using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Short-lived cache for the Maxio plan catalog.
/// </summary>
/// <remarks>
/// The catalog changes rarely but is read on every plan listing and on every subscribe (to
/// validate the requested handle and to report its price). Caching it keeps a burst of shoppers
/// from spending the site's four concurrent API slots on the same answer. Single-flight per key:
/// on a miss, exactly one caller loads and the rest wait for that load rather than stampeding
/// Maxio. Registered as a singleton so the cache and its gates are process-wide.
/// </remarks>
public sealed class MaxioCatalogCache
{
    private readonly IMemoryCache _cache;

    // Gates are per key, not shared: one loader may legitimately need a different cached value
    // while it runs, and a single gate would deadlock on that nested read.
    private readonly KeyedAsyncLock _loadGates = new();

    public MaxioCatalogCache(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrLoadAsync<T>(
        string key,
        TimeSpan timeToLive,
        Func<CancellationToken, Task<T>> loader,
        CancellationToken cancellationToken)
        where T : class
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            return await loader(cancellationToken).ConfigureAwait(false);
        }

        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        using (await _loadGates.AcquireAsync(key, cancellationToken).ConfigureAwait(false))
        {
            // Another caller may have populated the entry while this one queued.
            if (_cache.TryGetValue(key, out cached) && cached is not null)
            {
                return cached;
            }

            var loaded = await loader(cancellationToken).ConfigureAwait(false);
            _cache.Set(key, loaded, timeToLive);
            return loaded;
        }
    }

    /// <summary>Drops a cached entry so the next read reloads it from Maxio.</summary>
    public void Invalidate(string key) => _cache.Remove(key);
}
