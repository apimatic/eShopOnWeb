using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing, which is the
/// billing system of record. All state lives in Maxio; nothing is persisted locally.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription (Maxio products of the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent, keyed by the user id reference),
    /// then enrolls them into the requested plan. Subscribing twice to the same plan never
    /// creates a second subscription: the existing one is returned instead.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions as known by Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
