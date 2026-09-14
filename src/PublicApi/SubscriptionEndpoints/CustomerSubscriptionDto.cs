using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}
