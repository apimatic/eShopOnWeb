using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing system of record.
/// </summary>
public record CustomerSubscription(
    int Id,
    string State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CreatedAt,
    int? CustomerId)
{
    public decimal? Price => PriceInCents / 100m;
}
