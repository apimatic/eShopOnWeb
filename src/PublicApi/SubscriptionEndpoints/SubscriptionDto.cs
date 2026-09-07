using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
