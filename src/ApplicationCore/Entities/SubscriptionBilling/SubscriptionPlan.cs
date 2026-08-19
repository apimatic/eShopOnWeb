namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public class SubscriptionPlan
{
    public int? ProductId { get; init; }
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool RequireCreditCard { get; init; }
}
