using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing (the system of record). This is an
/// additive capability parallel to the existing one-time Catalog/Basket/Order flow.
/// Implementations translate provider failures into <see cref="Exceptions.SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in the plan identified by <paramref name="planHandle"/>. Ensures a Maxio
    /// customer exists for the shopper (idempotent by user id), and does not create a second subscription
    /// when an equivalent active one already exists.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions as reflected in Maxio.</summary>
    Task<IReadOnlyList<CustomerSubscription>> GetMySubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}
