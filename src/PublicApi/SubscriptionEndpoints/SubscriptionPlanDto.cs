namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can subscribe to. The stable <see cref="Handle"/> is what
/// clients pass back when subscribing.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval unit (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Number of interval units between renewals.</summary>
    public int IntervalCount { get; set; }

    public bool RequiresPaymentMethod { get; set; }
}
