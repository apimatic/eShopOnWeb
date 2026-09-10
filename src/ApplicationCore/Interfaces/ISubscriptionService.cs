using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, backed by Maxio Advanced Billing.
/// This is an additive, parallel capability to the existing one-time commerce flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given shopper in the plan identified by <paramref name="planHandle"/>.
    /// Ensures a single Maxio customer exists for the shopper and that a repeated request (e.g. a
    /// double-click) does not create a duplicate customer or subscription.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's current subscriptions. Returns an empty list if the shopper has no
    /// Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
