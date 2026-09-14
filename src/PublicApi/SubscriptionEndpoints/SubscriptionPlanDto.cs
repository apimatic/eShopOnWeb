namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier used to subscribe to this plan.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Number of interval units in one billing period.</summary>
    public int? Interval { get; set; }

    /// <summary>Billing period unit, for example <c>month</c>.</summary>
    public string? IntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }

    public decimal? TrialPrice { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when a payment method must be captured before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? ExpirationInterval { get; set; }

    public string? ExpirationIntervalUnit { get; set; }
}
