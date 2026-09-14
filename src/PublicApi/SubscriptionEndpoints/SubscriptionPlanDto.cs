namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan available for purchase.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring amount in dollars (e.g. 299.00) for one billing interval.</summary>
    public decimal? Price { get; set; }

    public int? IntervalCount { get; set; }

    /// <summary>Interval unit wire value, e.g. "month".</summary>
    public string? Interval { get; set; }
}
