using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool AlreadySubscribed { get; set; }
}
