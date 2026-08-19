namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string ProductHandle { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }
}
