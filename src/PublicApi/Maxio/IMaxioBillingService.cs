using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record SubscribeResult(SubscriptionDto Subscription, bool Created);

/// <summary>
/// Subscription billing operations backed by Maxio Advanced Billing (system of record).
/// Maxio is keyed off the eShopOnWeb user id (customer <c>reference</c>), so no local
/// persistence is required and the mapping survives restarts.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans (products) of the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the user, then subscribes
    /// them to the plan. A double-click returns the existing subscription instead
    /// of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current Maxio subscriptions of the user (empty when the user has
    /// never been enrolled with the billing system).
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels one of the user's own subscriptions immediately.
    /// </summary>
    Task<SubscriptionDto> CancelSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default);
}
