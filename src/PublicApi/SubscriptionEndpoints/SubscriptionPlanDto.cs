namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionPlanDto
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int IntervalDays { get; init; }
}
