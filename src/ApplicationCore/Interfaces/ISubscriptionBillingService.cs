using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record
/// (Maxio Advanced Billing). This is an additive, parallel capability to the
/// existing one-time commerce flow.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the products of the configured
    /// product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the <paramref name="subscriber"/> in the plan identified by
    /// <paramref name="planHandle"/>. Idempotent: ensures a single billing customer
    /// exists for the subscriber and does not create a duplicate subscription when an
    /// active one already exists for the same plan.
    /// </summary>
    /// <exception cref="UnknownSubscriptionPlanException">
    /// The plan handle is not part of the configured product family.
    /// </exception>
    Task<SubscriptionDetails> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriber's current subscriptions. Returns an empty list when the
    /// subscriber has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
