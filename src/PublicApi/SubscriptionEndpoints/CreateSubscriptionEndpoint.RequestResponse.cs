namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = "eshop-pro";
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
    public int CustomerId { get; set; }
}
