namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle (e.g. "eshop-pro"), used as the subscribe target.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Human-friendly plan name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Numeric product id in the billing system.</summary>
    public int ProductId { get; set; }

    /// <summary>Recurring price in integer minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units (e.g. dollars) for display.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
