namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan as returned to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable Maxio product handle - the value to pass when subscribing.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing interval.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Number of interval units between renewals.
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// "month" or "day".
    /// </summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public bool Taxable { get; set; }
}