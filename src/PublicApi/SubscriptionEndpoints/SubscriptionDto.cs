using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public SubscriptionPlanDto Plan { get; set; } = new();
    public DateTimeOffset? NextBillingAt { get; set; }
}
