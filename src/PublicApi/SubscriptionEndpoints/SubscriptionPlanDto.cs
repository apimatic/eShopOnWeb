namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API view of a subscription plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle used to subscribe (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units (dollars), for convenience.</summary>
    public decimal Price { get; set; }

    public string Currency { get; set; } = "USD";

    /// <summary>Billing interval unit (e.g. <c>month</c>).</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>Number of interval units per billing period.</summary>
    public int? IntervalCount { get; set; }
}
