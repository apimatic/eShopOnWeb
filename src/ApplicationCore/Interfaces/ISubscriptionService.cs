using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// Additive to the one-time Catalog/Basket/Order flow; it shares no state with it.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans currently offered, cheapest first.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the shopper and enrols them on the plan.
    /// Safe to call repeatedly: concurrent or repeated calls converge on a single customer and a
    /// single subscription per plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// The shopper's subscriptions, newest first. Returns an empty collection when the shopper has
    /// never subscribed (no billing customer exists yet).
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
