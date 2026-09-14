namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan as offered to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Pass this value as planHandle when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900 for $299.00.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price formatted for display, e.g. "299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Billing period length, combined with intervalUnit, e.g. 1 month.</summary>
    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the shopper must have a stored payment method before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
