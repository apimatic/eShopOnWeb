namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int BillingIntervalCount { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}
