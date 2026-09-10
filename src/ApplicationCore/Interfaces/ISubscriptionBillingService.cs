using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio Advanced Billing).
/// This is the only seam the rest of the app depends on; no billing-SDK type crosses it.
/// Implementations translate provider failures into <see cref="SubscriptionBillingException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the user (idempotent) and enrolls them in a plan
    /// (idempotent). A repeated call for the same user+plan returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriptionEnrollmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's subscriptions as held by the billing system, or an empty list when the
    /// user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscriptionInfo>> GetSubscriptionsForUserAsync(string userIdentity, CancellationToken cancellationToken = default);
}
