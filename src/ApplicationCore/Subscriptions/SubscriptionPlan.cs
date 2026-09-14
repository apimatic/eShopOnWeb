namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; } = 1;
    public string IntervalUnit { get; set; } = "month";
    public bool PaymentMethodRequired { get; set; }
}
