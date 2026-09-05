using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the recurring-subscription hero flow (browse plans, subscribe, view own
/// subscriptions) on top of Maxio, which is the system of record - eShopOnWeb keeps no local
/// copy of subscription state.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userName"/> and enrolls it in the
    /// plan identified by <paramref name="planHandle"/>. If the user already has a live
    /// subscription to that plan, that subscription is returned instead of creating a new one.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Returns an empty list when the user has no Maxio customer yet (never subscribed).</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userName, CancellationToken cancellationToken = default);
}
