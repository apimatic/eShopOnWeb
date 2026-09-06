namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int BillingIntervalDays { get; set; }
    public string? BillingInterval { get; set; }
    public bool RequiresCreditCard { get; set; }
}
