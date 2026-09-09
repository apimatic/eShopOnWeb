namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (Maxio product) offered to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceInCents { get; set; }

    /// <summary>
    /// Human-readable price, e.g. "$299.00".
    /// </summary>
    public string Price { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// True for the plan subscribed to when the caller does not specify one.
    /// </summary>
    public bool IsDefault { get; set; }
}
