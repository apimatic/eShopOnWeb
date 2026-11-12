using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class PlanList
{
    public List<PlanDto> Plans { get; set; } = new();

    /// <summary>False when the page cap stopped the walk before the family's list was exhausted.</summary>
    public bool IsComplete { get; set; } = true;
}

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Maxio subscription state (e.g. active), or "pending" while Maxio's answer is not yet known.</summary>
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>True when the data is the last stored copy because Maxio could not be reached.</summary>
    public bool IsStale { get; set; }
}
