using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public decimal Balance => BalanceInCents / 100m;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
}
