namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Handle { get; init; } = string.Empty;
    public decimal Price { get; init; }
}
