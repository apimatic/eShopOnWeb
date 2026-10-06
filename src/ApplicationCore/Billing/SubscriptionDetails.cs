using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's subscription as the billing system reports it.
/// </summary>
public record SubscriptionDetails(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CreatedAt)
{
    public decimal? Price => PriceInCents / 100m;
}
