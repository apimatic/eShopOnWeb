using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = "";
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal MonthlyPrice { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
