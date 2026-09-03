namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }

    /// <summary>Recurring price in the plan's currency (normalised from Maxio's integer cents).</summary>
    public decimal Price { get; set; }

    /// <summary>Raw price in cents as reported by Maxio.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int? Interval { get; set; }

    /// <summary>Interval unit as reported by Maxio (e.g. "month").</summary>
    public string IntervalUnit { get; set; }
}
