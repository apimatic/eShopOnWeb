using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by Maxio Advanced Billing as the
/// system of record. This is an additive, parallel capability to the one-time commerce
/// flow (Catalog → Basket → Order); it does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (the products in the configured product family).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in the given plan. Idempotent: ensures a single Maxio customer
    /// exists for the user and returns any existing live subscription to the same plan instead
    /// of creating a duplicate, so a double-click never creates two customers/subscriptions.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriber's subscriptions. Read-only: if no Maxio customer exists yet for
    /// the user, returns an empty collection without creating one.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
