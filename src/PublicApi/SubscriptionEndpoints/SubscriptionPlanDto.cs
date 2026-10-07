namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan offered by the billing system.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Unit price in cents for one billing interval.</summary>
    public long PriceInCents { get; set; }
    /// <summary>Unit price in the account currency (e.g. USD).</summary>
    public decimal Price { get; set; }
    /// <summary>Number of interval units between billings.</summary>
    public int Interval { get; set; }
    /// <summary>"day" or "month" — the unit of <see cref="Interval"/>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;
}
