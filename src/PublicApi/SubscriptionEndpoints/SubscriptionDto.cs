using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string BuyerId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? AutomaticallyResumeAt { get; set; }
    public SubscriptionPlanDto Plan { get; set; }
}
