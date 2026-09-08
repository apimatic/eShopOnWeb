using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans available in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the user and subscribes them to the given plan.
    /// Idempotent: when the user is already subscribed to a live subscription for that plan the
    /// existing subscription is returned instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string appUserId, string email, string productHandle, CancellationToken cancellationToken);

    /// <summary>Lists the caller's subscriptions as currently recorded by Maxio.</summary>
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string appUserId, string email, CancellationToken cancellationToken);
}

/// <summary>Outcome of a subscribe request.</summary>
public class SubscribeResult
{
    /// <summary>Set when a new subscription was created on Maxio.</summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}
