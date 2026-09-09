namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle used as the <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; set; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int IntervalCount { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
