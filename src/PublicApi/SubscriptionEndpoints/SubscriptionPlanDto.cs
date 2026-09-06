namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Identifier to pass as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? IntervalLength { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when subscribing to this plan requires a payment method on file first.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
