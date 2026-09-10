namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a subscription plan.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    /// <summary>Formatted price for display, e.g. "$299.00 / month".</summary>
    public string PriceDisplay { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}
