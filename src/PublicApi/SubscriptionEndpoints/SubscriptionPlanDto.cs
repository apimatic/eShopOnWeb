namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A purchasable subscription plan exposed to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public int ProductId { get; set; }

    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public int Interval { get; set; } = 1;

    public string IntervalUnit { get; set; } = "month";

    public bool HasTrial { get; set; }

    public bool RequiresPaymentMethod { get; set; }
}
