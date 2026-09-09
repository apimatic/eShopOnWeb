using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing against the billing system of record (Maxio Advanced Billing).
/// Additive to the one-time commerce flow; keyed on stable, deterministic references so every
/// operation is idempotent.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans (products) in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a billing customer exists for the given application user (idempotent), then
    /// subscribes them to <paramref name="planHandle"/> (idempotent: a double subscribe returns
    /// the existing subscription). When <paramref name="planHandle"/> is null, the cheapest plan
    /// in the configured product family is used.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(string userId, string userName, string? planHandle, CancellationToken ct);

    /// <summary>
    /// Lists the billing subscriptions belonging to the given application user. Returns an empty
    /// list when the user has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListMySubscriptionsAsync(string userId, CancellationToken ct);
}
