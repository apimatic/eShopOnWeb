using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
