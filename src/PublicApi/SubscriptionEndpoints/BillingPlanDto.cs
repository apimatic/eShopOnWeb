namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class BillingPlanDto
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}
