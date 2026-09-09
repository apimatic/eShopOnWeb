namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available to shoppers, sourced from Maxio Advanced Billing products
/// in the configured product family.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int PriceInCents { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;
}
