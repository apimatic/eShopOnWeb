using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="Truncated">True when the plan walk hit its page cap, so the list may be incomplete.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public record SubscriptionSummary(
    int? SubscriptionId,
    string? State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt);

public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Subscribes the user to the plan. Idempotent per (user, plan).</summary>
    Task<SubscriptionSummary> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
