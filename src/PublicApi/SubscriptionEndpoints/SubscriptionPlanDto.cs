namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount (major units).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month", "day").</summary>
    public string? IntervalUnit { get; set; }
}
