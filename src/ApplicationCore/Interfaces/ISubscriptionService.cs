using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability for eShopOnWeb shoppers. Runs alongside the one-time
/// basket/order flow rather than replacing it.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper on a plan, creating their billing customer record first if needed.
    /// Idempotent: repeating the call returns the existing subscription instead of creating a second.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions, newest first. Returns an empty list when the shopper has
    /// never subscribed; it does not create a billing customer as a side effect.
    /// </summary>
    Task<IReadOnlyCollection<BillingSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
