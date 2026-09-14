namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; the value to send when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in whole currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the plan cannot be subscribed to without a payment method on file.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
