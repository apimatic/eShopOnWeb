using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A subscription plan (Maxio product) available for subscribing.</summary>
public sealed record SubscriptionPlanInfo(
    int ProductId,
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

/// <summary>The state of a user's subscription as confirmed by Maxio.</summary>
public sealed record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string Currency,
    string State,
    DateTime? NextBillingDate,
    DateTime? CurrentPeriodEnd,
    DateTime? CreatedAt)
{
    /// <summary>True when the subscribe operation found an existing live subscription instead of creating one (idempotent replay).</summary>
    public bool AlreadySubscribed { get; init; }
}

/// <summary>
/// Application-level subscription orchestration on top of Maxio Advanced Billing:
/// ensures a Maxio customer exists for the eShopOnWeb user (idempotently), enrolls
/// them into a plan without creating duplicates, and reads back their subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Lists subscription plans (Maxio products) in the configured product family.</summary>
    Task<Result<IReadOnlyList<SubscriptionPlanInfo>>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Subscribes the user to the plan with the given handle. Idempotent: if the user
    /// already holds a live subscription for the same plan, it is returned unchanged.
    /// </summary>
    Task<Result<SubscriptionSummary>> SubscribeAsync(
        string userId,
        string username,
        string email,
        string planHandle,
        CancellationToken cancellationToken);

    /// <summary>Lists all of the user's Maxio subscriptions. Empty when the user has no Maxio customer yet.</summary>
    Task<Result<IReadOnlyList<SubscriptionSummary>>> GetSubscriptionsForUserAsync(
        string userId,
        CancellationToken cancellationToken);
}
