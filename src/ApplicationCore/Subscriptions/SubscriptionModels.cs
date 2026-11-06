using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan offered by the billing system for the configured product family.</summary>
public record PlanInfo(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int? Interval,
    string? IntervalUnit);

/// <summary>The offered plans; <see cref="Truncated"/> is true when the read stopped at its page cap.</summary>
public record PlanListResult(IReadOnlyList<PlanInfo> Plans, bool Truncated);

public record SubscriptionInfo(
    long Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    decimal? Price,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? ActivatedAt);

/// <summary>The caller's subscriptions as the billing system holds them.</summary>
public record SubscriptionListResult(IReadOnlyList<SubscriptionInfo> Subscriptions);
