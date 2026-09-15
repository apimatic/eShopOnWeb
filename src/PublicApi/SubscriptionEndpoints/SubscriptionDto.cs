using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}
