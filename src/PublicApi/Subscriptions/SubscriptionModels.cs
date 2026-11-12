using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price => PriceInCents / 100m;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents / 100m;
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public string? Reference { get; set; }
}

public class PlanListResult
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();

    /// <summary>True when the page cap was hit and more plans may exist.</summary>
    public bool IsTruncated { get; set; }
}

public class SubscribeResult
{
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when the user was already subscribed to the plan (idempotent repeat).</summary>
    public bool Created { get; set; }
}
