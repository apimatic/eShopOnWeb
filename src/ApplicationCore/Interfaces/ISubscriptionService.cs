using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, parallel to the one-time catalog/basket/order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the shopper has a billing customer and is enrolled on the requested plan.
    /// Safe to call repeatedly: a shopper ends up with at most one live subscription per plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Every subscription the shopper holds. Empty when they have never subscribed.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
