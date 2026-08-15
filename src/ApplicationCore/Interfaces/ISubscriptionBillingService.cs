using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing system of record.
/// Implementations translate provider failures into
/// <see cref="Exceptions.SubscriptionBillingException"/> so callers have a single failure type.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available to enroll in.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a user in a plan. Idempotent: ensures a single billing customer exists for the
    /// user and reuses an existing live subscription for the same plan rather than duplicating it.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given user reference. Returns an empty list when
    /// the user has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
