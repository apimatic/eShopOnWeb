using Microsoft.eShopWeb.ApplicationCore.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing, which is the billing
/// system of record. All operations are idempotent with respect to the
/// (userId, plan) pair: a repeated call never creates a second customer or
/// subscription.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products) available in the configured
    /// product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the eShopOnWeb user, then subscribes
    /// them to the plan identified by <paramref name="planHandle"/>. If the user
    /// is already subscribed to that plan, the existing subscription is returned.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has
    /// no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListUserSubscriptionsAsync(string userId, CancellationToken ct);
}
