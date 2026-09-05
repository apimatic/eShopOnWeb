using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = default!;
    public string PlanName { get; set; } = default!;
    public decimal Price { get; set; }
    public string State { get; set; } = default!;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool AlreadyExisted { get; set; }
}
