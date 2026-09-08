namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (from the configured Maxio product family) as shown to a shopper.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Plan price in minor units (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Plan price in major units (dollars).</summary>
    public decimal Price { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }
}
