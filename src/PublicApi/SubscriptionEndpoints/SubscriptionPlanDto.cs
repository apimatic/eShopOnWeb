namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Post this back to <c>api/subscriptions</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price of one billing period, in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price of one billing period as a major-unit amount, for example 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Length of a billing period, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, for example <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Whether subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    public bool HasTrial { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
