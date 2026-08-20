namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
