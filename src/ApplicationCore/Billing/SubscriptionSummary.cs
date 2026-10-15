using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A subscription as the billing system of record reports it.</summary>
public sealed record SubscriptionSummary(
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
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    string? Reference);
