using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations, backed by an external billing system of record.
/// This is the domain-facing contract; the concrete implementation talks to Maxio Advanced
/// Billing. It is additive to (and independent of) the one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the recurring plans a shopper may subscribe to (the active products of the
    /// configured product family), cheapest first.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single billing customer exists for the shopper and never creates a
    /// second subscription when one is already active for the plan (safe under double-click).
    /// </summary>
    /// <exception cref="SubscriptionPlanNotFoundException">The plan handle is not offered.</exception>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the shopper's subscriptions as reported by the billing system, most recent first.
    /// Returns an empty list when the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
