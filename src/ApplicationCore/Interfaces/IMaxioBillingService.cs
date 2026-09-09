using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment (the products of the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user (idempotent) and enrolls
    /// them in the plan identified by <paramref name="planHandle"/>. Subscribing twice to the
    /// same plan returns the existing subscription instead of creating a second one.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions of the given eShopOnWeb user. Returns an empty list when
    /// the user has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
