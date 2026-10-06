using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capability backed by an external billing system of record
/// (Maxio Advanced Billing). Additive to the one-time commerce flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given eShopOnWeb user (idempotently)
    /// and enrolls them in the plan identified by <paramref name="planHandle"/>.
    /// Repeated calls for the same user and plan return the existing subscription
    /// instead of creating a duplicate.
    /// </summary>
    Task<UserSubscription> SubscribeAsync(SubscriptionUserData user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions owned by the given eShopOnWeb user.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> ListUserSubscriptionsAsync(SubscriptionUserData user, CancellationToken cancellationToken = default);
}