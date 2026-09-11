namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
}
