using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}
