namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public sealed record SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
}
