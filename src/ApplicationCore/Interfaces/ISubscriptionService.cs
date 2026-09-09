using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent) and subscribes
    /// them to the plan identified by <paramref name="productHandle"/>.
    /// If the user already holds a subscription for that plan, the existing
    /// subscription's status is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscriptionStatus> SubscribeAsync(string userId, string userName, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current status of all of the user's subscriptions, refreshed
    /// from Maxio where possible.
    /// </summary>
    Task<IReadOnlyList<SubscriptionStatus>> GetMySubscriptionsAsync(string userId, string userName, CancellationToken cancellationToken = default);
}
