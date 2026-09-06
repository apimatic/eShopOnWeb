using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, backed by an external billing system of record.
/// This is a parallel capability to the one-time Catalog/Basket/Order flow, not a replacement.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Plans a shopper can subscribe to, as published by the billing system.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in a plan, creating the billing customer first if one does not exist.
    /// Idempotent: repeating the same request never produces a second customer or a second
    /// subscription. See <see cref="SubscribeResult.Outcome"/> to tell a fresh signup from a replay.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription belonging to the subscriber, newest first. Returns an empty list when the
    /// subscriber has never been enrolled (no billing customer exists yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
