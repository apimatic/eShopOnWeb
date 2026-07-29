using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the external billing system of record (Maxio Advanced
/// Billing). This is an additive capability alongside the existing one-time commerce flow.
/// Implementations translate provider failures into <see cref="Exceptions.SubscriptionBillingException"/>
/// so callers face a single failure type carrying a caller-safe message and an HTTP status.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans available to subscribe to (the configured product family's products).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given user in the plan identified by <paramref name="planHandle"/>. Idempotent for
    /// the hero flow: it ensures a single Maxio customer exists for the user (lookup-or-create by
    /// stable reference) and does not create a second active subscription to the same plan, so a
    /// double-click yields the same subscription rather than a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's subscriptions. If no Maxio customer exists for the user yet, returns an
    /// empty list rather than creating one.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
