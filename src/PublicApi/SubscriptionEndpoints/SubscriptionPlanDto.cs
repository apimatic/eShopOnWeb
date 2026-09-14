namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; pass it to POST api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO-4217 currency code, e.g. USD.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Billing period length, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period: month or day.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// True when the plan cannot be subscribed to here because the billing system demands a stored
    /// payment method, which this API does not capture.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
