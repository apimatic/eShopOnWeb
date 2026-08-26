namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A subscription plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
