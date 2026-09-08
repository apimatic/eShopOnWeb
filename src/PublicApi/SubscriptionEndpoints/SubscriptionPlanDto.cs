namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan surfaced from the Maxio product catalog.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable Maxio product handle (e.g. "eshop-pro"). Use this when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the site currency (e.g. 299.00 for a $299/month plan).</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Billing interval (e.g. 1).</summary>
    public int Interval { get; set; } = 1;

    /// <summary>Billing interval unit: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = "month";

    /// <summary>The numeric id of the Maxio product. Handles are stable, ids are not.</summary>
    public long MaxioProductId { get; set; }

    public bool Taxable { get; set; }
}
