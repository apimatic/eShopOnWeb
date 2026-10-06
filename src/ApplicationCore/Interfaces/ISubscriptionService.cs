using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provides recurring-subscription billing backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available on the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent) and subscribes them to the
    /// given plan. Subscribing to the same plan more than once returns the existing subscription.
    /// </summary>
    Task<Subscription> SubscribeAsync(SubscriptionUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given eShopOnWeb user.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}
