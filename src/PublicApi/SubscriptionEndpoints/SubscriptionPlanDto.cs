namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle identifying the plan; this is what you post to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. <c>299.00</c>.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, e.g. <c>29900</c>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO currency code of the billing site, when the provider reports one.</summary>
    public string? Currency { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>True when the plan cannot be sold without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
