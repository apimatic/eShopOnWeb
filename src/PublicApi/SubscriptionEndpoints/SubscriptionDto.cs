using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
