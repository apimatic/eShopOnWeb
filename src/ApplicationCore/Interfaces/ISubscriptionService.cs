using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates subscription billing against Maxio Advanced Billing, which acts
/// as the billing system of record for recurring subscriptions.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for purchase (Maxio products in the
    /// configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanSummary>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given application user (idempotent,
    /// keyed by the user's id) and subscribes them to the requested plan. Subscribing
    /// twice to the same plan returns the existing subscription instead of creating
    /// a duplicate.
    /// </summary>
    Task<SubscriptionSummary> SubscribeAsync(string userId, string userName, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the Maxio subscriptions belonging to the Maxio customer mapped to the
    /// given application user. Returns an empty list when the user has no Maxio
    /// customer yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
