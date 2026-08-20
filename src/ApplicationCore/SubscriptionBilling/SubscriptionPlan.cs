namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequireCreditCard { get; init; }
}
