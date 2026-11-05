using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to a plan. Idempotent: repeating the call for a plan the user is already
    /// subscribed to returns the existing subscription instead of creating another.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
