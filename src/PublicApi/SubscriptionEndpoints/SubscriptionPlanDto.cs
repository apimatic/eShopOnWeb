namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Value to send as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price, in the currency below.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, for example <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the plan cannot be subscribed to without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string? ProductFamilyHandle { get; set; }
}
