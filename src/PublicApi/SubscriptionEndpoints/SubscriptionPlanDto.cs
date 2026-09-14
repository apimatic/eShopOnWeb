namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API projection of a subscribe-able plan.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ProductId { get; set; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string? Description { get; set; }
}
