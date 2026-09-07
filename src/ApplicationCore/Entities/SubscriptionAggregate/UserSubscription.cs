using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public int SubscriptionPlanId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
