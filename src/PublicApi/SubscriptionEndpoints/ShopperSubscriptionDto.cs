using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public string? Reference { get; set; }
}
