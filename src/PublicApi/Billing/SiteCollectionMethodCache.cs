using System;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Process-lifetime cache of the <c>payment_collection_method</c> the Maxio site's invoicing architecture
/// requires (Relationship Invoicing: remittance; legacy Statements: invoice). Registered as a singleton.
/// <list type="bullet">
/// <item>Only a resolved method is cached. A failed read, or a site whose architecture is indeterminate,
/// throws out of the resolver and leaves the cache empty, so the next request reads the site again.</item>
/// <item>Concurrent first requests are single-flighted: one reads the site, the others wait (each under its
/// own request budget) and then use the cached value.</item>
/// <item>The read itself is done by the caller-supplied resolver (<see cref="SubscriptionBillingService"/>),
/// so the SDK call and its error mapping stay in the one class that fronts the SDK.</item>
/// </list>
/// The site's architecture is a site-level setting; a change to it takes effect after a process restart.
/// </summary>
public sealed class SiteCollectionMethodCache : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CollectionMethod? _cached;

    /// <summary>The cached method, or null when it has not been resolved yet.</summary>
    public CollectionMethod? Cached => Volatile.Read(ref _cached);

    public async Task<CollectionMethod> GetOrResolveAsync(Func<CancellationToken, Task<CollectionMethod>> resolve, CancellationToken cancellationToken)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            cached = Volatile.Read(ref _cached);
            if (cached is not null)
            {
                return cached;
            }

            // Throws on failure or an indeterminate answer: nothing is cached then.
            var resolved = await resolve(cancellationToken);
            Volatile.Write(ref _cached, resolved);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
