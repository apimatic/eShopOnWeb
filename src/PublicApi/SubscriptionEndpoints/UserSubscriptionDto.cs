namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public string? Reference { get; set; }
}
