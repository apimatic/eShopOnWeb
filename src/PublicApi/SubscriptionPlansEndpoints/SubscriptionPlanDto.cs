namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

public class SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
}
