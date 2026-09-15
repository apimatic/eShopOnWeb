namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }
}
