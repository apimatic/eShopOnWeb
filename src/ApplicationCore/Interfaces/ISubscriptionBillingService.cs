using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record
/// (Maxio Advanced Billing). This capability is additive and parallel to the existing
/// one-time commerce flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Returns the subscription plans available for enrollment (the products in the configured
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given subscriber in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single billing customer exists for the subscriber (keyed by a stable
    /// reference) and reuses an existing live subscription to the same plan rather than creating a
    /// duplicate, so a double-click never creates two customers or two subscriptions.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriber's current subscriptions. Empty when the subscriber has no billing
    /// customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberInfo subscriber, CancellationToken cancellationToken = default);
}
