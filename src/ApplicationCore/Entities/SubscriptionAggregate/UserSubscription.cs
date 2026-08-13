using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity
{
    public string ApplicationUserId { get; set; } = null!;
    public long MaxioSubscriptionId { get; set; }
    public long MaxioProductId { get; set; }
    public int? MaxioPlanId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public decimal? Price { get; set; }
    public string State { get; set; } = null!;
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
