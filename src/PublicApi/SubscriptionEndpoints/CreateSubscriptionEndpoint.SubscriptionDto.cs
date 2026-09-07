namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public decimal Price { get; set; }
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public System.DateTimeOffset? ActivatedAt { get; set; }
}
