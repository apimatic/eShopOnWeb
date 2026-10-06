using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application service behind the subscription capability: browsable plans, the
/// (idempotent) subscribe flow, and the current shopper's subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the shopper and enrolls them in the plan.
    /// Calling this twice for the same shopper + plan returns the same subscription
    /// instead of creating a duplicate.
    /// </summary>
    Task<SubscribeOutcome> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Subscriptions owned by the shopper's billing customer (empty if never subscribed).</summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
