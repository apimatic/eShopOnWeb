namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price in currency units (dollars), derived from <see cref="PriceInCents"/>.</summary>
    public decimal? Price { get; set; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string? IntervalUnit { get; set; }
}
