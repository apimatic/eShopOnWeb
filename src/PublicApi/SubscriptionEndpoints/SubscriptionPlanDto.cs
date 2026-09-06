namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier used when subscribing. Numeric ids are deliberately not exposed.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, for clients that would rather not handle decimals.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 code, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? IntervalLength { get; set; }

    /// <summary>Unit of the billing period, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when signup requires a payment method to be captured first.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
