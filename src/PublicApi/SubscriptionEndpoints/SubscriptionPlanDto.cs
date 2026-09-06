namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record SubscriptionPlanDto
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public long? PriceInCents { get; set; }
    public int? BillingInterval { get; set; }
    public string? BillingIntervalUnit { get; set; }
}
