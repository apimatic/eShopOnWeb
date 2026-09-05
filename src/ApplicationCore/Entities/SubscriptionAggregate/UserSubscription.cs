using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public int SubscriptionPlanId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
