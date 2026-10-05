using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capabilities backed by the external billing
/// system of record (Maxio Advanced Billing).
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for the given user, then enrolls
    /// that user into the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: repeated calls for the same user and plan return the
    /// existing subscription instead of creating a second one.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string displayName,
        string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the given user holds in the billing system.
    /// Does not create anything; returns an empty list when the user has
    /// no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForUserAsync(string userId,
        string email, string displayName, CancellationToken cancellationToken = default);
}