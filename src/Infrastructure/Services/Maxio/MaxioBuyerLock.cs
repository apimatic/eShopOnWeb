using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Maxio has no server-side dedup primitive for "create a subscription for this
/// customer+product" (confirmed: no idempotency-key field exists on the Create Subscription
/// model in Maxio's official SDK). To still guarantee a same-process double-click can't create
/// two subscriptions, <see cref="MaxioBillingService.SubscribeAsync"/> serializes per-buyer via
/// the semaphore handed out here, on top of its check-existing-subscriptions-first logic.
/// Registered as a singleton so the lock is shared across requests.
/// </summary>
internal sealed class MaxioBuyerLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim For(string buyerReference) =>
        _locks.GetOrAdd(buyerReference, _ => new SemaphoreSlim(1, 1));
}
