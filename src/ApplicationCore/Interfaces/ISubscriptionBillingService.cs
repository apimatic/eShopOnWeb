using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the system of record. This is an additive, parallel
/// capability to the one-time Catalog/Basket/Order flow and is intentionally persistence-ignorant:
/// the billing provider (not the local database) is the source of truth for customers and subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to, from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the given user to a plan. Ensures a billing customer exists for the user and enrolls
    /// them, idempotently: a repeated call (e.g. a double-click) never creates a second customer or a
    /// second active subscription to the same plan — the existing subscription is returned instead.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's subscriptions as reported by the billing system. Empty if the user has never
    /// been provisioned as a billing customer.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}
