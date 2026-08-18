namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? CreatedAt { get; set; }
}
