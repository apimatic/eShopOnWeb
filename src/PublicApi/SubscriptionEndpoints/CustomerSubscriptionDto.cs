using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
