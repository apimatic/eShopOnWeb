namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// A subscription plan (Maxio product) as offered by the configured product family.
/// The plan <see cref="Handle"/> is the stable identifier clients should send when subscribing.
/// </summary>
public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Price in dollars per billing interval.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    /// <summary>"month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";
}
