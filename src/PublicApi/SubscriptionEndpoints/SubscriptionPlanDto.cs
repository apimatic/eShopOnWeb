namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Pass this to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major currency units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in cents, which is how the billing system stores it.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Length of a billing period, expressed as a count of <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; set; }

    /// <summary>"day" or "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human-readable billing cadence, e.g. "$299.00 / month".</summary>
    public string PriceDescription { get; set; } = string.Empty;

    public bool HasTrial { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public decimal? TrialPrice { get; set; }

    public decimal? SetupFee { get; set; }

    /// <summary>True when a payment method has to be captured before subscribing to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? PricePointName { get; set; }
}
