using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    public required string UserId { get; set; }
    public required int SubscriptionPlanId { get; set; }
    public SubscriptionPlan? SubscriptionPlan { get; set; }
    public required int MaxioSubscriptionId { get; set; }
    public required int MaxioCustomerId { get; set; }
    public required string State { get; set; }
    public DateTime? CurrentPeriodStartAt { get; set; }
    public DateTime? CurrentPeriodEndAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
