namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int PriceInCents { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
}
