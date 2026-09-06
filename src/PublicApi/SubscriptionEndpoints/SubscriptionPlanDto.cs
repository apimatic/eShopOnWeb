namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier for the plan. Pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in major currency units.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>ISO currency code of the billing site, when it could be read.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when this plan cannot be subscribed to until a payment method has been captured.</summary>
    public bool PaymentMethodRequired { get; set; }

    public bool HasTrial { get; set; }

    public string? PricePointHandle { get; set; }

    public string? ProductFamilyHandle { get; set; }
}
