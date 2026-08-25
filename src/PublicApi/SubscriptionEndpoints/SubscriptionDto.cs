using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
