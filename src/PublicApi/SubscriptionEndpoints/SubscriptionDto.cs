using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal PlanPrice { get; set; }
    public long PlanPriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public bool IsActive { get; set; }
}
