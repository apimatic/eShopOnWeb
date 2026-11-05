using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

public record SubscribeResult(SubscriptionSummary Subscription, bool Created);

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. Parallel to the one-time Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Idempotent per (user, plan): a repeated call returns the existing subscription.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
