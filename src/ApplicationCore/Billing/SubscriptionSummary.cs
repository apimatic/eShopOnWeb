using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A billing subscription as confirmed by the billing provider.</summary>
public sealed record SubscriptionSummary(
    int? SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    int? CustomerId)
{
    public decimal? Price => PriceInCents is long cents ? cents / 100m : null;
}
