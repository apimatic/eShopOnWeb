namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscribable plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}
