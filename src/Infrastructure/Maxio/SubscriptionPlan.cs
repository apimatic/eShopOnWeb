namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A subscription plan (product) available for purchase.</summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequiresPaymentMethod { get; set; }
}
