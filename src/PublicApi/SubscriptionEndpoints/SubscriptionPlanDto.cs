namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The Maxio product handle. Pass this as planHandle when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in dollars.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in cents as recorded by Maxio.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval length, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    public bool HasTrial { get; set; }

    /// <summary>Whether Maxio requires a payment method at signup for this plan.</summary>
    public bool RequireCreditCard { get; set; }
}
