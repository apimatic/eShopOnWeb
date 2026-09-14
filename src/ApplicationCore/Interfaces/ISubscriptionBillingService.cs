using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability, with Maxio Advanced Billing as the
/// system of record. Implementations must be idempotent with respect to the user:
/// repeated calls for the same user/plan must never create duplicate customers or
/// duplicate subscriptions.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans that are available for subscription.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the user (idempotent), then enrolls the
    /// user in the plan identified by its stable handle. If the user already has a
    /// live subscription to the plan, the existing enrollment is returned unchanged.
    /// </summary>
    Task<UserSubscription> SubscribeAsync(BillingUserInfo user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all of the user's enrollments. Returns an empty list if the user has
    /// never been enrolled (does not create a billing customer).
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(BillingUserInfo user, CancellationToken cancellationToken = default);
}
