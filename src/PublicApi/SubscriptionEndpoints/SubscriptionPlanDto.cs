namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to. <see cref="Handle"/> is what a subscribe request names -
/// billing-system numeric ids are not stable and are deliberately not exposed.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price, in the billing site's currency.</summary>
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }

    /// <summary>Billing period, e.g. 1 "month".</summary>
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
