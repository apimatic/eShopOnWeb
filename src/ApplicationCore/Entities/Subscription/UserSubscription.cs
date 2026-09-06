using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscription;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public long MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceInCents { get; set; }
    public string State { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
