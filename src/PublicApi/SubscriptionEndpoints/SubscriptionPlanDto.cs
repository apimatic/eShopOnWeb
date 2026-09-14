namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Post this value to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price per billing period, in the minor unit of <see cref="Currency"/>.</summary>
    public int PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. <c>USD</c>.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Length of a billing period, counted in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>True when subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
