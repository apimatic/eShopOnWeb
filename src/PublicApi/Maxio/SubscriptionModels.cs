using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <param name="Truncated">True when the plan list was cut short at the page cap.</param>
public record PlansResult(IReadOnlyList<PlanDto> Plans, bool Truncated);

/// <param name="AlreadySubscribed">True when the user already held a live subscription to the plan, so nothing was created.</param>
public record SubscribeResult(SubscriptionDto Subscription, bool AlreadySubscribed);
