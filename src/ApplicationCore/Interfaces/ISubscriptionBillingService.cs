using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Gateway to the subscription billing system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans currently offered in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the subscriber and enrolls them in the given plan.
    /// Idempotent: repeated calls for the same subscriber and plan return the existing
    /// active subscription instead of creating duplicates.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions belonging to the subscriber identified by the given reference.
    /// Returns an empty list when the subscriber has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
