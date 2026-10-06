using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case orchestration for recurring subscriptions backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Plans currently available for subscription (from the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes the given user to a plan: ensures a Maxio customer exists for the
    /// user (never creating duplicates), enrolls them in the plan, and mirrors the result.
    /// A second call with the same user + live plan returns the existing subscription (Created=false).
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's subscriptions as recorded by Maxio (the billing system of record),
    /// refreshing the local mirror as a side effect.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}

public record SubscribeResult(UserSubscription Subscription, bool Created)
{
}
