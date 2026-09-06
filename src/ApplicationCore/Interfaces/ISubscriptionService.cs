using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability for eShopOnWeb shoppers. Runs alongside the existing
/// one-time Catalog/Basket/Order flow and does not replace any part of it.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user onto a plan, creating the billing customer first if needed. Idempotent: a
    /// repeated request never produces a second customer or a second subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions held by a user. Returns an empty list when the user has never
    /// subscribed and therefore has no billing customer record.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
