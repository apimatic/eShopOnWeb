namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A subscription plan available for shoppers to subscribe to.
/// Sourced from the billing system of record (Maxio Advanced Billing product catalog).
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public bool RequireCreditCard { get; set; }
}
