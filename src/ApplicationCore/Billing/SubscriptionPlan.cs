namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
