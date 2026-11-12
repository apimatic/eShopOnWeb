using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class PlanList
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    /// <summary>True when the page cap was hit with a full last page: the list may be incomplete.</summary>
    public bool Truncated { get; set; }
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string Reference { get; set; } = string.Empty;
    /// <summary>False when Maxio could not be read; the item then reflects only what the app stored.</summary>
    public bool Available { get; set; } = true;
    /// <summary>"pending" | "completed" | "unknown": the local claim status.</summary>
    public string ClaimStatus { get; set; } = string.Empty;
}
