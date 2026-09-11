namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string? NextBillingAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public int CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
}
