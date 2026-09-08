using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing, the billing
/// system of record for subscriptions. All operations are idempotent per user
/// and plan: repeating a call never creates a second customer or subscription.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for the user, then subscribes them to the
    /// given plan (the configured default when <paramref name="planHandle"/> is null).
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string email, string userName,
        string? planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has no
    /// billing customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetUserSubscriptionsAsync(string userId,
        CancellationToken cancellationToken);
}
