namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan offered to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing period (e.g. 299.00).
    /// </summary>
    public decimal Price { get; set; }

    public int Interval { get; set; } = 1;
    public string IntervalUnit { get; set; } = "month";
}
