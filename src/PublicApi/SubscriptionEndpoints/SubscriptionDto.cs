using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
