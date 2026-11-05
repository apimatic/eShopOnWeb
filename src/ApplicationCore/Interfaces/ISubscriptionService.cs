using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the user to a plan: ensures a billing customer exists for the user and
    /// returns the existing live subscription to the plan, if any, instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
