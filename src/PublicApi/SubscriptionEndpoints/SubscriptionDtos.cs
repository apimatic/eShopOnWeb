using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

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
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents / 100m;

    /// <summary>Maxio subscription state (open set, e.g. "active"); null when it could not be refreshed.</summary>
    public string? State { get; set; }

    /// <summary>Maxio next_assessment_at, falling back to current_period_ends_at when absent.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public string? Reference { get; set; }
    public int? CustomerId { get; set; }
}

public class SubscriptionPlanList
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();

    /// <summary>False when the page cap was hit and more plans may exist.</summary>
    public bool IsComplete { get; set; } = true;
}
