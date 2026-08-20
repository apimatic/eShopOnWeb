using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
}
