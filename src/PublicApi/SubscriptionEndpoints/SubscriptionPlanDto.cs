namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Pass this back as <c>planHandle</c> when creating a subscription.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major units (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, free of rounding.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Renewal cadence, e.g. 1 with "month".</summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable cadence, e.g. "$299.00 / month".</summary>
    public string PriceDescription { get; set; } = string.Empty;

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    public decimal? SetupFee { get; set; }

    /// <summary>
    /// When true this plan cannot be subscribed to through this API: it needs a stored payment
    /// method and eShopOnWeb does not capture one.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }
}
