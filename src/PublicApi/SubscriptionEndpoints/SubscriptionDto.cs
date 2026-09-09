using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public string? Reference { get; set; }
    public bool AlreadySubscribed { get; set; }
}
