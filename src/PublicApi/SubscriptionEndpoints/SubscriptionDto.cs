using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
