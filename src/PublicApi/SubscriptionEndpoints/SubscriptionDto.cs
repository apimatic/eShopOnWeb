using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal PlanPrice { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public string? NextPlanHandle { get; set; }
}
