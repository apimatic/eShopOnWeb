namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available to shoppers, projected for the API.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>Numerical billing interval (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (e.g. "month").</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public bool RequiresPaymentMethod { get; set; }
}
