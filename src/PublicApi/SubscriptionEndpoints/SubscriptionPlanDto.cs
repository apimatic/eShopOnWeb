namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan (a Maxio product) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of interval units between billings (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit (e.g. month).</summary>
    public string IntervalUnit { get; set; } = "month";
}
