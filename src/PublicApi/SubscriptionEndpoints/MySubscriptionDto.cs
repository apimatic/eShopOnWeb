namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string State { get; set; } = "";
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public decimal? Amount { get; set; }
}
