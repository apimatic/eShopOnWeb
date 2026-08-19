namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public int Interval { get; init; }
    public bool RequiresPaymentMethod { get; init; }
}
