namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier to send back on <c>POST /api/subscriptions</c>.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the plan cannot be subscribed to without a payment method on file.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
