using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
}
