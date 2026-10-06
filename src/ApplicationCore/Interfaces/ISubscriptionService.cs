using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application service behind the subscription capability: browsing plans,
/// idempotent enrollment, and viewing one's own subscriptions. Maxio
/// Advanced Billing remains the system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Plans the app is configured to sell (products of the configured
    /// Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the shopper has a Maxio customer (idempotent) and enrolls them
    /// in the given plan. Replays (e.g. a double-clicked Subscribe) return the
    /// already-active subscription instead of creating a second one.
    /// </summary>
    Task<SubscriptionProvisioningResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscriptions owned by the shopper's Maxio customer; empty when the
    /// shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
