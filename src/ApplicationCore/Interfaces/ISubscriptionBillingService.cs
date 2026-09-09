using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio Advanced Billing).
/// This is an additive capability, parallel to the one-time Catalog/Basket/Order flow. All
/// implementations translate provider failures into
/// <see cref="Exceptions.BillingException"/> so callers have a single failure type to handle.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given user in the plan identified by <paramref name="planHandle"/>. Ensures a
    /// billing customer exists for the user first (idempotent by <see cref="BillingUserIdentity.Reference"/>),
    /// and returns any pre-existing non-terminal subscription for that plan rather than creating a
    /// duplicate — so a repeated submit never yields two customers or two subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUserIdentity user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscriptionInfo>> GetSubscriptionsForUserAsync(BillingUserIdentity user, CancellationToken cancellationToken = default);
}
