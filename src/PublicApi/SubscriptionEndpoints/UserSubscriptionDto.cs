using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal? Price { get; set; }
    public string State { get; set; } = null!;
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
