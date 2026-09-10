namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in the site's currency's major unit (e.g. dollars).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string Interval { get; set; } = "month";

    /// <summary>Number of <see cref="Interval"/>s per billing period.</summary>
    public int IntervalCount { get; set; }

    public bool RequiresPaymentMethod { get; set; }
}
