namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan offered to shoppers, as read from the billing system of record.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The stable plan handle in Maxio Advanced Billing; pass it to POST /api/subscriptions.
    /// </summary>
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    /// <summary>
    /// The recurring price formatted as a decimal string (e.g. "299.00").
    /// </summary>
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    /// <summary>
    /// A human-readable summary of the billing cadence (e.g. "$299.00 per month").
    /// </summary>
    public string PriceSummary { get; set; } = string.Empty;
}
