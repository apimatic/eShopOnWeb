namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionPlanDto
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
}
