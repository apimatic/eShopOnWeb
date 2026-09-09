using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Subscription billing operations backed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscribable plans of the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlanInfo>> GetPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotently keyed on the user id)
    /// and subscribes them to the plan. Repeated calls with the same user and plan return
    /// the existing subscription instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscriptionInfo> SubscribeAsync(MaxioSubscriber subscriber, string planHandle, CancellationToken ct);

    /// <summary>
    /// Lists the subscriptions the user already holds. Does not create a Maxio customer
    /// as a side effect; a user without one simply has no subscriptions.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId, CancellationToken ct);
}
