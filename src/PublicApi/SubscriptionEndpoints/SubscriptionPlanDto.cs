namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (a Maxio product belonging to the configured product family).
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Price of the plan's default price point in major currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Price of the plan's default price point in cents.</summary>
    public long PriceInCents { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
