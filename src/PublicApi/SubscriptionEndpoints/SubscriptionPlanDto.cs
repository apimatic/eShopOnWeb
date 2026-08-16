namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to (a Maxio product in the configured family).
/// </summary>
public class SubscriptionPlanDto
{
    public int ProductId { get; set; }

    /// <summary>Stable API handle; use this as the plan identifier when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }
}
