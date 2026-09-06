using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// This capability runs alongside the existing one-time Catalog/Basket/Order flow; it does not
/// replace any part of it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans the shopper may subscribe to, newest provider state each call.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes <paramref name="subscriber"/> to <paramref name="planHandle"/>, creating the
    /// billing customer first if one does not already exist.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key. When supplied, repeated calls carrying the same key resolve to
    /// the same subscription. When omitted the operation is still idempotent: an existing live
    /// subscription to the same plan is adopted rather than duplicated.
    /// </param>
    /// <returns>The subscription, and whether this call is what created it.</returns>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to <paramref name="subscriber"/>, most recent first.
    /// Returns an empty list when the shopper has no billing customer yet — that is not an error.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
