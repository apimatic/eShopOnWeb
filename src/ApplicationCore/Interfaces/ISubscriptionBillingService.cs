using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record
/// (Maxio Advanced Billing). This is an additive, parallel capability to the
/// existing one-time commerce flow and does not touch Basket/Order.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to, from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a customer exists for the given eShopOnWeb user (idempotent) and enrolls
    /// them in the plan identified by <paramref name="planHandle"/>. If the customer is
    /// already actively enrolled in that plan, the existing subscription is returned
    /// instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to the given eShopOnWeb user.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
