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

public record SubscriptionSummary(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

/// <param name="IsTruncated">True when the provider list was cut short by a safety cap.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

/// <param name="IsTruncated">True when the result may be incomplete.</param>
public record SubscriptionList(IReadOnlyList<SubscriptionSummary> Subscriptions, bool IsTruncated);

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionSummary> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    Task<SubscriptionList> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
