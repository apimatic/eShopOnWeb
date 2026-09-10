using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the configured billing system of record
/// (Maxio Advanced Billing). This is an additive capability, parallel to the existing
/// one-time Catalog / Basket / Order flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the active products in the configured
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to the plan identified by <paramref name="planHandle"/>.
    /// Ensures a single billing customer exists for the user and does not create a second
    /// subscription when the user is already actively subscribed to that plan (idempotent).
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user. Returns an empty list when the
    /// user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
