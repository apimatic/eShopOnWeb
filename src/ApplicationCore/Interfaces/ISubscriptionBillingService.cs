using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing.
/// Implementations must be idempotent: repeating a call must never create
/// duplicate customers or subscriptions at the provider.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a billing customer exists for the user and subscribes them to the plan.
    /// If the user already has a live subscription to the same plan, that subscription
    /// is returned instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userId, string email, string productHandle, CancellationToken ct = default);

    /// <summary>Lists the user's subscriptions. Empty when the user has no billing customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken ct = default);
}
