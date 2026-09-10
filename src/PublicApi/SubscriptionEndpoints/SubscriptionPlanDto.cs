namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Price in the billing system's minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Price as a decimal amount, for display.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of interval units per billing period (e.g. 1).</summary>
    public int IntervalCount { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string? IntervalUnit { get; set; }
}
