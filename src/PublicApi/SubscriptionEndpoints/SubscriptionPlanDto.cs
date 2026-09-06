namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
    public int BillingIntervalDays { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
}
