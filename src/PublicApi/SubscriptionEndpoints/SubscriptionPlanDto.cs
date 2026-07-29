namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price per billing period, in the major currency unit.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of interval units per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Interval unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }
}
