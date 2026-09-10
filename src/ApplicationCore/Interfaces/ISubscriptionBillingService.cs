using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record. This is an
/// additive capability alongside the existing one-time commerce flow; it does not touch Catalog/Basket/Order.
/// Implementations raise <see cref="SubscriptionBillingException"/> for every failure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans (products in the configured Maxio product family).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the subscriber (idempotent) and subscribes them to the plan
    /// identified by <paramref name="planHandle"/>. Idempotent: a live subscription to the same plan is
    /// returned rather than creating a duplicate, so a double-click never creates two customers/subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions for the given user (by their Maxio customer <c>reference</c>). Returns an
    /// empty list when the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default);
}
