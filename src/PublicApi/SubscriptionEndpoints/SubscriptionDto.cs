namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public string State { get; set; }
    public string NextBillingAt { get; set; }
    public decimal CurrentPrice { get; set; }
}
